#!/bin/bash
# Assertion helper functions for E2E tests

source "$(dirname "${BASH_SOURCE[0]}")/common.sh"

# Assert that response contains a string
assert_contains() {
    local haystack="$1"
    local needle="$2"
    local message="${3:-Response contains '$needle'}"
    
    if [[ "$haystack" == *"$needle"* ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Expected to find: '$needle'"
        log_error "In response: '$haystack'"
        return 1
    fi
}

# Assert that response does NOT contain a string
assert_not_contains() {
    local haystack="$1"
    local needle="$2"
    local message="${3:-Response does not contain '$needle'}"
    
    if [[ "$haystack" != *"$needle"* ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Did not expect to find: '$needle'"
        log_error "In response: '$haystack'"
        return 1
    fi
}

# Assert that two strings are equal
assert_equals() {
    local expected="$1"
    local actual="$2"
    local message="${3:-Strings are equal}"
    
    if [[ "$expected" == "$actual" ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Expected: '$expected'"
        log_error "Actual: '$actual'"
        return 1
    fi
}

# Assert that response time is less than threshold
assert_response_time() {
    local max_time="$1"
    local message="${2:-Response time < ${max_time}s}"
    
    if (( $(echo "$RESPONSE_TIME < $max_time" | bc -l) )); then
        log_assertion "PASS" "$message (${RESPONSE_TIME}s)"
        return 0
    else
        log_assertion "FAIL" "$message (${RESPONSE_TIME}s >= ${max_time}s)"
        return 1
    fi
}

# Assert that a number is greater than another
assert_greater_than() {
    local actual="$1"
    local threshold="$2"
    local message="${3:-Value is greater than $threshold}"
    
    if (( $(echo "$actual > $threshold" | bc -l) )); then
        log_assertion "PASS" "$message ($actual > $threshold)"
        return 0
    else
        log_assertion "FAIL" "$message ($actual <= $threshold)"
        return 1
    fi
}

# Assert that a number is less than another
assert_less_than() {
    local actual="$1"
    local threshold="$2"
    local message="${3:-Value is less than $threshold}"
    
    if (( $(echo "$actual < $threshold" | bc -l) )); then
        log_assertion "PASS" "$message ($actual < $threshold)"
        return 0
    else
        log_assertion "FAIL" "$message ($actual >= $threshold)"
        return 1
    fi
}

# Assert that response matches a regex pattern
assert_matches() {
    local haystack="$1"
    local pattern="$2"
    local message="${3:-Response matches pattern}"
    
    if [[ "$haystack" =~ $pattern ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Pattern: '$pattern'"
        log_error "Response: '$haystack'"
        return 1
    fi
}

# Assert that a file exists
assert_file_exists() {
    local filepath="$1"
    local message="${2:-File exists: $filepath}"
    
    if [[ -f "$filepath" ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "File not found: $filepath"
        return 1
    fi
}

# Assert that a file does NOT exist
assert_file_not_exists() {
    local filepath="$1"
    local message="${2:-File does not exist: $filepath}"
    
    if [[ ! -f "$filepath" ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "File exists but should not: $filepath"
        return 1
    fi
}

# Assert that a directory exists
assert_dir_exists() {
    local dirpath="$1"
    local message="${2:-Directory exists: $dirpath}"
    
    if [[ -d "$dirpath" ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Directory not found: $dirpath"
        return 1
    fi
}

# Assert JSON field value
assert_json_field() {
    local json="$1"
    local field="$2"
    local expected="$3"
    local message="${4:-JSON field '$field' equals '$expected'}"
    
    local actual=$(echo "$json" | jq -r "$field")
    
    if [[ "$actual" == "$expected" ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Expected: '$expected'"
        log_error "Actual: '$actual'"
        return 1
    fi
}

# Assert that response is not empty
assert_not_empty() {
    local value="$1"
    local message="${2:-Response is not empty}"

    if [[ -n "$value" && "$value" != "null" ]]; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "Response is empty or null"
        return 1
    fi
}

# Assert that JSON is valid
assert_json_valid() {
    local json="$1"
    local message="${2:-JSON is valid}"
    
    if echo "$json" | jq empty 2>/dev/null; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "JSON is not valid"
        return 1
    fi
}

# Assert that JSON contains a value
assert_json_contains() {
    local json="$1"
    local value="$2"
    local message="${3:-JSON contains '$value'}"
    
    if echo "$json" | jq -e ". | tostring | contains(\"$value\")" > /dev/null 2>&1; then
        log_assertion "PASS" "$message"
        return 0
    else
        log_assertion "FAIL" "$message"
        log_error "JSON does not contain: $value"
        return 1
    fi
}

# Assert that JSON array has at least N elements
assert_json_count() {
    local json="$1"
    local path="$2"
    local min_count="$3"
    local message="${4:-JSON has at least $min_count elements}"
    
    local actual=$(echo "$json" | jq "$path | length")
    
    if [[ "$actual" -ge "$min_count" ]]; then
        log_assertion "PASS" "$message ($actual >= $min_count)"
        return 0
    else
        log_assertion "FAIL" "$message ($actual < $min_count)"
        return 1
    fi
}

# Export functions
export -f assert_contains assert_not_contains assert_equals
export -f assert_response_time assert_greater_than assert_less_than
export -f assert_matches assert_file_exists assert_file_not_exists
export -f assert_dir_exists assert_json_field assert_not_empty
export -f assert_json_valid assert_json_contains assert_json_count
