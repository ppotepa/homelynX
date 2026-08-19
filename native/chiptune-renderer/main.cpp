/*
 * Homelynx Furnace renderer adapter
 * Copyright (C) 2026 Homelynx contributors
 *
 * This file is distributed under GPL-2.0-or-later because it is linked with
 * Furnace. See THIRD_PARTY_NOTICES.md and the Furnace license in the image.
 */
#include <algorithm>
#include <cmath>
#include <fstream>
#include <iostream>
#include <string>
#include "pch.h"
#include "ta-log.h"
#include "engine/engine.h"
#include "engine/instrument.h"
#include "engine/sample.h"
#include <nlohmann/json.hpp>

using json = nlohmann::json;

// The engine references the application's error sink even in a headless build.
void reportError(String what) {
  logE("%s", what);
  std::cerr << what << '\n';
}

template<typename T> static T bounded(T value, T low, T high) {
  return value < low ? low : (value > high ? high : value);
}

static DivSystem systemFor(const std::string& chip) {
  if (chip == "gb" || chip == "gbc" || chip == "gameboy") return DIV_SYSTEM_GB;
  if (chip == "nes") return DIV_SYSTEM_NES;
  if (chip == "snes") return DIV_SYSTEM_SNES;
  if (chip == "sms") return DIV_SYSTEM_SMS;
  if (chip == "c64_6581") return DIV_SYSTEM_C64_6581;
  if (chip == "c64_8580") return DIV_SYSTEM_C64_8580;
  if (chip == "genesis") return DIV_SYSTEM_YM2612;
  if (chip == "pce") return DIV_SYSTEM_PCE;
  if (chip == "atari2600") return DIV_SYSTEM_TIA;
  if (chip == "pokey") return DIV_SYSTEM_POKEY;
  if (chip == "pcspeaker") return DIV_SYSTEM_PCSPKR;
  if (chip == "zx_spectrum") return DIV_SYSTEM_SFX_BEEPER;
  throw std::runtime_error("unsupported chip: " + chip);
}

static DivSample* makeSample(int voice) {
  const int count = 512;
  DivSample* sample = new DivSample();
  sample->name = "Homelynx generated sample";
  sample->depth = DIV_SAMPLE_DEPTH_16BIT;
  sample->centerRate = 16726;
  sample->loop = true;
  sample->loopStart = 0;
  sample->loopEnd = count;
  if (!sample->init(count)) throw std::runtime_error("could not allocate SNES sample");
  uint32_t noise = 0x7fffU ^ (uint32_t)(voice * 977);
  for (int i = 0; i < count; ++i) {
    double phase = (double)i / count;
    double value;
    if (voice >= 3 && voice <= 5) {
      uint32_t bit = ((noise >> 0) ^ (noise >> 1)) & 1U;
      noise = (noise >> 1) | (bit << 14);
      value = (noise & 1U) ? .65 : -.65;
    } else if (voice == 2) value = std::sin(phase * 2.0 * M_PI);
    else if (voice == 7) value = 1.0 - 2.0 * phase;
    else value = phase < .5 ? .72 : -.72;
    sample->data16[i] = (short)(value * 30000.0);
  }
  return sample;
}

static void configureInstruments(DivEngine& engine, const std::string& chip, const json& request) {
  std::string wave = request.value("wave", "square");
  int duty = bounded(request.value("duty", 25), 1, 99);
  int attack = bounded(request.value("attack", 0), 0, 31);
  int decay = bounded(request.value("decay", 8), 0, 31);
  int sustain = bounded(request.value("sustain", 12), 0, 31);
  int release = bounded(request.value("release", 8), 0, 31);
  int voices = chip == "snes" ? 8 : chip == "genesis" ? 6 : chip == "pce" ? 6 : chip == "c64_6581" || chip == "c64_8580" ? 3 : chip == "pcspeaker" || chip == "zx_spectrum" || chip == "atari2600" ? 1 : 4;
  for (int voice = 0; voice < voices; ++voice) {
    int sampleIndex = -1;
    if (chip == "snes") sampleIndex = engine.addSamplePtr(makeSample(voice));
    int instrumentIndex = engine.addInstrument(voice);
    if (instrumentIndex < 0) throw std::runtime_error("could not create instrument");
    DivInstrument* instrument = engine.song.ins[instrumentIndex];
    instrument->name = "Homelynx voice " + std::to_string(voice + 1);
    if (chip == "gb" || chip == "gbc" || chip == "gameboy") {
      instrument->type = DIV_INS_GB;
      instrument->gb.envVol = 15;
      instrument->gb.envDir = 0;
      instrument->gb.envLen = chip == "gbc" ? (voice == 0 ? 1 : 3) : (voice == 0 ? 2 : 4);
      instrument->gb.soundLen = 64;
      instrument->gb.softEnv = chip == "gbc";
      instrument->gb.alwaysInit = true;
    } else if (chip == "genesis") {
      instrument->type = DIV_INS_FM;
      instrument->fm.alg = wave == "fm" ? (voice % 3 == 0 ? 4 : 0) : 0;
      instrument->fm.fb = 4;
      instrument->fm.op[0].ar = 31; instrument->fm.op[0].dr = 8; instrument->fm.op[0].rr = 5; instrument->fm.op[0].tl = 18; instrument->fm.op[0].mult = 1;
      instrument->fm.op[1].ar = 31; instrument->fm.op[1].dr = 12; instrument->fm.op[1].rr = 6; instrument->fm.op[1].tl = 32; instrument->fm.op[1].mult = 2;
      instrument->fm.op[2].ar = 31; instrument->fm.op[2].dr = 10; instrument->fm.op[2].rr = 5; instrument->fm.op[2].tl = 42; instrument->fm.op[2].mult = 1;
      instrument->fm.op[3].ar = 31; instrument->fm.op[3].dr = 8; instrument->fm.op[3].rr = 5; instrument->fm.op[3].tl = 8; instrument->fm.op[3].mult = 1;
    } else if (chip == "c64_6581" || chip == "c64_8580") {
      instrument->type = DIV_INS_C64;
      instrument->c64.triOn = wave == "triangle" || (wave == "square" && voice % 3 == 1); instrument->c64.sawOn = wave == "saw" || (wave == "square" && voice % 3 != 1); instrument->c64.pulseOn = wave == "square" || wave == "fm";
      instrument->c64.a = (unsigned char)attack; instrument->c64.d = (unsigned char)decay; instrument->c64.s = (unsigned char)sustain; instrument->c64.r = (unsigned char)release; instrument->c64.duty = (unsigned short)(duty * 4095 / 100);
      instrument->c64.toFilter = voice == 1; instrument->c64.lp = voice == 1; instrument->c64.cut = 900; instrument->c64.res = 5;
    } else if (chip == "snes") {
      instrument->type = DIV_INS_SNES;
      instrument->amiga.useSample = true;
      instrument->amiga.initSample = sampleIndex;
      instrument->snes.useEnv = true;
      instrument->snes.a = (unsigned char)attack;
      instrument->snes.d = (unsigned char)decay;
      instrument->snes.s = (unsigned char)sustain;
      instrument->snes.r = (unsigned char)release;
    } else if (chip == "pce") {
      instrument->type = DIV_INS_PCE;
    } else if (chip == "pokey") {
      instrument->type = DIV_INS_POKEY;
    } else if (chip == "pcspeaker" || chip == "zx_spectrum") {
      instrument->type = DIV_INS_BEEPER;
    } else if (chip == "atari2600") {
      instrument->type = DIV_INS_TIA;
    }
  }
  if (chip == "snes") engine.renderSamples();
}

static void fillSong(DivEngine& engine, const json& request) {
  DivSubSong* sub = engine.song.subsong.at(0);
  int bpm = bounded(request.value("bpm", 140), 40, 300);
  long endTick = request.value("endTick", 960L);
  constexpr int maxRows = 255 * 256;
  // Use 1/64-note rows for normal songs. For unusually long MIDI files,
  // reduce resolution only as much as needed to fit Furnace's order table.
  int ticksPerRow = std::max(60, (int)std::ceil(endTick / (double)maxRows));
  sub->hz = (float)bpm * 16.0f / ticksPerRow;
  sub->speeds.len = 1;
  sub->speeds.val[0] = 1;
  int endRow = std::max(1, (int)std::ceil(endTick / (double)ticksPerRow));
  // Keep the full Furnace capacity for genuinely long compositions, but do
  // not export every short song as a 256-row pattern.  The latter adds a
  // silent tail of roughly 27 seconds at 140 BPM.
  sub->patLen = endRow <= 256 ? bounded(endRow + 1, 2, 256) : 256;
  if (endRow > maxRows) throw std::runtime_error("MIDI is larger than Furnace tracker capacity (255 patterns x 256 rows).");
  sub->ordersLen = std::max(1, (endRow + sub->patLen - 1) / sub->patLen);
  for (int channel = 0; channel < engine.song.chans; ++channel)
    for (int order = 0; order < sub->ordersLen; ++order)
      sub->orders.ord[channel][order] = (unsigned char)order;

  for (const auto& item : request.at("notes")) {
    int voice = item.value("voice", 0);
    if (voice < 0 || voice >= engine.song.chans) continue;
    long startTick = item.value("startTick", 0L);
    long durationTick = std::max(1L, item.value("durationTick", 240L));
    int startRow = std::max(0, (int)std::llround(startTick / (double)ticksPerRow));
    int endNoteRow = std::max(startRow + 1, (int)std::llround((startTick + durationTick) / (double)ticksPerRow));
    if (startRow >= sub->ordersLen * sub->patLen) continue;
    int order = startRow / sub->patLen, row = startRow % sub->patLen;
    DivPattern* pattern = sub->pat[voice].getPattern(order, true);
    pattern->newData[row][DIV_PAT_NOTE] = (short)bounded(item.value("pitch", 60) + 48, 0, 179);
    pattern->newData[row][DIV_PAT_INS] = (short)voice;
    int maxVolume = engine.getMaxVolumeChan(voice);
    int velocity = bounded(item.value("velocity", 100), 1, 127);
    pattern->newData[row][DIV_PAT_VOL] = (short)bounded((velocity * maxVolume + 63) / 127, 1, maxVolume);
    if (endNoteRow < sub->ordersLen * sub->patLen) {
      int offOrder = endNoteRow / sub->patLen, offRow = endNoteRow % sub->patLen;
      DivPattern* offPattern = sub->pat[voice].getPattern(offOrder, true);
      if (offPattern->newData[offRow][DIV_PAT_NOTE] == -1)
        offPattern->newData[offRow][DIV_PAT_NOTE] = DIV_NOTE_OFF;
    }
  }
  engine.song.name = "Homelynx chiptune";
  engine.song.author = "Homelynx";
  engine.calcSongTimestamps();
  engine.syncReset();
}

int main(int argc, char** argv) {
  try {
    if (argc == 2 && std::string(argv[1]) == "--version") {
      std::cout << "homelynx-chiptune-renderer furnace " << DIV_VERSION << "\n";
      return 0;
    }
    if (argc != 2) throw std::runtime_error("usage: homelynx-chiptune-renderer OUTPUT.wav");
    json request; std::cin >> request;
    std::string chip = request.value("chip", "gb");
    DivEngine engine;
    engine.preInit(true);
    engine.setAudio(DIV_AUDIO_DUMMY);
    if (!engine.init()) throw std::runtime_error("could not initialize Furnace engine");
    if (!engine.changeSystem(0, systemFor(chip), false)) throw std::runtime_error("could not select Furnace system");
    configureInstruments(engine, chip, request);
    fillSong(engine, request);
    DivAudioExportOptions options;
    options.sampleRate = request.value("sampleRate", 44100);
    options.chans = 2;
    options.loops = 0;
    options.format = DIV_EXPORT_FORMAT_WAV;
    options.wavFormat = DIV_EXPORT_WAV_S16;
    engine.setConsoleMode(true);
    if (!engine.saveAudio(argv[1], options)) throw std::runtime_error("Furnace rejected audio export");
    engine.waitAudioFile();
    engine.everythingOK();
    std::cout << "{\"success\":true,\"backend\":\"furnace\"}\n" << std::flush;
    // Furnace's headless engine teardown may wait on an audio thread after the
    // exporter has already joined and closed the WAV. Avoid that second teardown.
    std::_Exit(0);
  } catch (const std::exception& ex) {
    std::cerr << ex.what() << "\n" << std::flush;
    std::_Exit(1);
  }
}
