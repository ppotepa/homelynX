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
  if (chip == "gameboy") return DIV_SYSTEM_GB;
  if (chip == "nes") return DIV_SYSTEM_NES;
  if (chip == "snes") return DIV_SYSTEM_SNES;
  if (chip == "sms") return DIV_SYSTEM_SMS;
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

static void configureInstruments(DivEngine& engine, const std::string& chip) {
  int voices = chip == "snes" ? 8 : 4;
  for (int voice = 0; voice < voices; ++voice) {
    int sampleIndex = -1;
    if (chip == "snes") sampleIndex = engine.addSamplePtr(makeSample(voice));
    int instrumentIndex = engine.addInstrument(voice);
    if (instrumentIndex < 0) throw std::runtime_error("could not create instrument");
    DivInstrument* instrument = engine.song.ins[instrumentIndex];
    instrument->name = "Homelynx voice " + std::to_string(voice + 1);
    if (chip == "snes") {
      instrument->type = DIV_INS_SNES;
      instrument->amiga.useSample = true;
      instrument->amiga.initSample = sampleIndex;
      instrument->snes.useEnv = true;
      instrument->snes.a = 12;
      instrument->snes.d = 5;
      instrument->snes.s = 6;
      instrument->snes.r = 12;
    }
  }
  if (chip == "snes") engine.renderSamples();
}

static void fillSong(DivEngine& engine, const json& request) {
  DivSubSong* sub = engine.song.subsong.at(0);
  int bpm = bounded(request.value("bpm", 140), 40, 300);
  sub->hz = (float)bpm / 15.0f; // one tracker row is one sixteenth note
  sub->speeds.len = 1;
  sub->speeds.val[0] = 1;
  long endTick = request.value("endTick", 960L);
  int endRow = std::max(1, (int)std::ceil(endTick / 240.0));
  sub->patLen = endRow <= 256 ? bounded(endRow + 1, 2, 256) : 64;
  sub->ordersLen = bounded((endRow + sub->patLen - 1) / sub->patLen, 1, 255);
  for (int channel = 0; channel < engine.song.chans; ++channel)
    for (int order = 0; order < sub->ordersLen; ++order)
      sub->orders.ord[channel][order] = (unsigned char)order;

  for (const auto& item : request.at("notes")) {
    int voice = item.value("voice", 0);
    if (voice < 0 || voice >= engine.song.chans) continue;
    long startTick = item.value("startTick", 0L);
    long durationTick = std::max(60L, item.value("durationTick", 240L));
    int startRow = std::max(0, (int)std::llround(startTick / 240.0));
    int endNoteRow = std::max(startRow + 1, (int)std::llround((startTick + durationTick) / 240.0));
    if (startRow >= sub->ordersLen * sub->patLen) continue;
    int order = startRow / sub->patLen, row = startRow % sub->patLen;
    DivPattern* pattern = sub->pat[voice].getPattern(order, true);
    pattern->newData[row][DIV_PAT_NOTE] = (short)bounded(item.value("pitch", 60) + 48, 0, 179);
    pattern->newData[row][DIV_PAT_INS] = (short)voice;
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
    std::string chip = request.value("chip", "gameboy");
    DivEngine engine;
    engine.preInit(true);
    engine.setAudio(DIV_AUDIO_DUMMY);
    if (!engine.init()) throw std::runtime_error("could not initialize Furnace engine");
    if (!engine.changeSystem(0, systemFor(chip), false)) throw std::runtime_error("could not select Furnace system");
    configureInstruments(engine, chip);
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
