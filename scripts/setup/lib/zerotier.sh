# shellcheck shell=bash

configure_zerotier() {
  local enabled
  local network_id
  local install_if_missing
  local node_id
  local network_status
  local dns_enabled

  enabled="$(get_env_value ZEROTIER_ENABLED)"
  network_id="$(get_env_value ZEROTIER_NETWORK_ID)"
  install_if_missing="$(get_env_value ZEROTIER_INSTALL_IF_MISSING)"
  dns_enabled="$(get_env_value ZEROTIER_DNS_ENABLED)"

  if ! is_truthy "$enabled" && [ -z "$network_id" ]; then
    log "ZeroTier remote access is not configured. Set ZEROTIER_NETWORK_ID in .env to enable it."
    return
  fi

  if [ -z "$network_id" ]; then
    warn "ZEROTIER_ENABLED is true, but ZEROTIER_NETWORK_ID is empty. Skipping ZeroTier join."
    return
  fi

  if ! command -v zerotier-cli >/dev/null 2>&1; then
    if is_truthy "$install_if_missing"; then
      log "Installing ZeroTier One."
      require_command curl
      if [ "$(id -u)" -eq 0 ]; then
        curl -fsSL https://install.zerotier.com | bash
      else
        command -v sudo >/dev/null 2>&1 || {
          warn "sudo is required to install ZeroTier automatically. Install ZeroTier manually and rerun setup."
          return
        }
        curl -fsSL https://install.zerotier.com | sudo bash
      fi
    else
      warn "ZeroTier is not installed. Install zerotier-one manually or set ZEROTIER_INSTALL_IF_MISSING=true."
      return
    fi
  fi

  if command -v systemctl >/dev/null 2>&1; then
    as_root systemctl enable --now zerotier-one >/dev/null 2>&1 || warn "Could not enable/start zerotier-one via systemctl."
  fi

  log "Joining ZeroTier network $network_id."
  as_root zerotier-cli join "$network_id" >/dev/null 2>&1 || warn "ZeroTier join returned a non-zero status; it may already be joined."
  node_id="$(as_root zerotier-cli info 2>/dev/null | awk '{print $3}' || true)"
  network_status="$(as_root zerotier-cli listnetworks 2>/dev/null | awk -v id="$network_id" '$3 == id {print $6, $9}' || true)"

  log "ZeroTier node id: ${node_id:-unknown}. Authorize this node in my.zerotier.com if it is not authorized yet."
  if [ -n "$network_status" ]; then
    log "ZeroTier network status: $network_status"
  else
    warn "ZeroTier network is joined but no assigned IP is visible yet. Authorize the node and wait a few seconds."
  fi

  as_root zerotier-cli set "$network_id" allowDNS=1 >/dev/null 2>&1 || warn "Could not enable ZeroTier allowDNS for this network."

  if is_truthy "$dns_enabled"; then
    configure_zerotier_dns "$network_id"
  fi
}
install_dnsmasq_if_missing() {
  if command -v dnsmasq >/dev/null 2>&1; then
    return 0
  fi

  log "Installing dnsmasq for ZeroTier private DNS."
  if command -v apt-get >/dev/null 2>&1; then
    as_root apt-get update
    as_root apt-get install -y dnsmasq
  elif command -v dnf >/dev/null 2>&1; then
    as_root dnf install -y dnsmasq
  elif command -v pacman >/dev/null 2>&1; then
    as_root pacman -Sy --noconfirm dnsmasq
  else
    warn "dnsmasq is not installed and setup does not know this package manager. Install dnsmasq manually."
    return 1
  fi
}
configure_zerotier_dns() {
  local network_id="$1"
  local dns_domain
  local server_ip
  local zt_iface
  local assigned_ip
  local network_status
  local network_json
  local dnsmasq_path
  local config_tmp
  local service_tmp

  dns_domain="$(get_env_value ZEROTIER_DNS_DOMAIN)"
  dns_domain="${dns_domain:-homelynx.zt}"
  server_ip="$(get_env_value ZEROTIER_DNS_SERVER_IP)"

  require_command python3
  network_json="$(as_root zerotier-cli -j listnetworks 2>/dev/null || true)"
  read -r zt_iface assigned_ip network_status < <(
    ZT_NETWORKS_JSON="$network_json" python3 - "$network_id" <<'PY'
import json
import os
import sys

network_id = sys.argv[1]
try:
    networks = json.loads(os.environ.get("ZT_NETWORKS_JSON", "[]"))
except Exception:
    networks = []

for network in networks:
    if network.get("nwid") == network_id or network.get("id") == network_id:
        addresses = network.get("assignedAddresses") or []
        assigned_ip = addresses[0].split("/", 1)[0] if addresses else ""
        print(network.get("portDeviceName", ""), assigned_ip, network.get("status", ""))
        break
PY
  )

  if [ -z "$server_ip" ]; then
    server_ip="$assigned_ip"
    [ -n "$server_ip" ] && set_env_value ZEROTIER_DNS_SERVER_IP "$server_ip"
  fi

  if [ -z "$assigned_ip" ] || [ -z "$zt_iface" ]; then
    warn "ZeroTier DNS is enabled, but network $network_id has no assigned IP yet (status: ${network_status:-unknown}). Authorize the node, wait for an assigned IP, then rerun setup."
    return
  fi

  if [ "$server_ip" != "$assigned_ip" ]; then
    warn "ZEROTIER_DNS_SERVER_IP is $server_ip, but this node currently has $assigned_ip. Using the assigned IP."
    server_ip="$assigned_ip"
    set_env_value ZEROTIER_DNS_SERVER_IP "$server_ip"
  fi

  install_dnsmasq_if_missing || return
  dnsmasq_path="$(command -v dnsmasq)"

  config_tmp="$(mktemp)"
  service_tmp="$(mktemp)"

  cat > "$config_tmp" <<DNS_EOF
no-hosts
no-resolv
domain-needed
bogus-priv
bind-dynamic
interface=$zt_iface
except-interface=lo
listen-address=$server_ip
local=/$dns_domain/
address=/$dns_domain/$server_ip
DNS_EOF

  cat > "$service_tmp" <<SERVICE_EOF
[Unit]
Description=Homelynx ZeroTier DNS
After=network-online.target zerotier-one.service
Wants=network-online.target
Requires=zerotier-one.service

[Service]
Type=simple
ExecStart=$dnsmasq_path --keep-in-foreground --conf-file=/etc/homelynx/zt-dnsmasq.conf --pid-file=/run/homelynx-zt-dns.pid
Restart=on-failure
RestartSec=2

[Install]
WantedBy=multi-user.target
SERVICE_EOF

  as_root mkdir -p /etc/homelynx
  if command -v systemctl >/dev/null 2>&1; then
    as_root systemctl disable --now gomelynx-zt-dns.service >/dev/null 2>&1 || true
  fi
  as_root rm -f /etc/systemd/system/gomelynx-zt-dns.service /etc/gomelynx/zt-dnsmasq.conf
  as_root install -m 0644 "$config_tmp" /etc/homelynx/zt-dnsmasq.conf
  as_root install -m 0644 "$service_tmp" /etc/systemd/system/homelynx-zt-dns.service
  rm -f "$config_tmp" "$service_tmp"

  if command -v systemctl >/dev/null 2>&1; then
    as_root systemctl disable --now dnsmasq >/dev/null 2>&1 || true
    as_root systemctl daemon-reload
    as_root systemctl enable --now homelynx-zt-dns.service >/dev/null
    as_root systemctl restart homelynx-zt-dns.service
    log "ZeroTier DNS is serving *.$dns_domain from $server_ip on $zt_iface."
  else
    warn "systemctl is not available. DNS config was written, but homelynx-zt-dns.service was not started."
  fi
}
