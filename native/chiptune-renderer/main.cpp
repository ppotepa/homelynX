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
#include <map>
#include <string>
#include <vector>
#include "pch.h"
#include "ta-log.h"
#include "engine/engine.h"
#include "engine/instrument.h"
#include "engine/sample.h"
#include <nlohmann/json.hpp>

using json = nlohmann::json;

void reportError(String what) {
  logE("%s", what);
  std::cerr << what << '\n';
}

template<typename T> static T bounded(T value, T low, T high) {
  return value < low ? low : (value > high ? high : value);
}

static bool isPercussionPatch(const std::string& patch) {
  return patch == "drums" || patch == "kick" || patch == "snare" || patch == "hat" ||
         patch == "open_hat" || patch == "tom" || patch == "crash" || patch == "ride";
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

static DivInstrumentType instrumentTypeFor(const std::string& chip, const std::string& voiceClass) {
  if (chip == "gb" || chip == "gbc" || chip == "gameboy") return DIV_INS_GB;
  if (chip == "nes") return DIV_INS_NES;
  if (chip == "snes") return DIV_INS_SNES;
  if (chip == "sms") return DIV_INS_STD;
  if (chip == "c64_6581" || chip == "c64_8580") return DIV_INS_C64;
  if (chip == "genesis") return voiceClass == "psg" || voiceClass == "noise" ? DIV_INS_STD : DIV_INS_FM;
  if (chip == "pce") return DIV_INS_PCE;
  if (chip == "atari2600") return DIV_INS_TIA;
  if (chip == "pokey") return DIV_INS_POKEY;
  if (chip == "pcspeaker" || chip == "zx_spectrum") return DIV_INS_BEEPER;
  return DIV_INS_STD;
}

static DivSample* makeSample(int voice, const std::string& patch) {
  const bool percussion = isPercussionPatch(patch);
  const int count = percussion
    ? (patch == "kick" ? 512 : patch == "hat" ? 384 : patch == "snare" ? 768 : 1024)
    : 64;
  DivSample* sample = new DivSample();
  sample->name = "Homelynx SNES patch " + patch;
  sample->depth = DIV_SAMPLE_DEPTH_16BIT;
  // Furnace's SNES documentation recommends ~16.744 kHz and a 64-sample
  // single-cycle waveform so C-4 is tuned as middle C. The former 1024-sample
  // tonal cycle was about four octaves too low.
  sample->centerRate = 16744;
  sample->loop = !percussion;
  sample->loopStart = 0;
  sample->loopEnd = count;
  if (!sample->init(count)) throw std::runtime_error("could not allocate SNES sample");
  uint32_t noise = 0x7fffU ^ (uint32_t)(voice * 977);
  for (int i = 0; i < count; ++i) {
    double phase = (double)i / count;
    double value;
    if (patch == "kick") {
      // Fast low-frequency transient with a falling envelope.
      double envelope = std::pow(1.0 - phase, 2.5);
      double cycles = 5.0 * phase - 2.5 * phase * phase;
      value = std::sin(cycles * 2.0 * M_PI) * envelope;
    } else if (percussion) {
      uint32_t bit = ((noise >> 0) ^ (noise >> 1)) & 1U;
      noise = (noise >> 1) | (bit << 14);
      double decayPower = patch == "open_hat" || patch == "crash" || patch == "ride" ? 1.3 : 2.5;
      double envelope = std::pow(1.0 - phase, decayPower);
      value = (noise & 1U) ? .65 * envelope : -.65 * envelope;
    } else if (patch == "bass") {
      value = (1.0 - 2.0 * phase) * .65 + std::sin(phase * 2.0 * M_PI) * .25;
    } else if (patch == "bell") {
      value = std::sin(phase * 2.0 * M_PI) * .62 +
              std::sin(phase * 3.0 * 2.0 * M_PI) * .23 +
              std::sin(phase * 5.0 * 2.0 * M_PI) * .15;
    } else if (patch == "strings" || patch == "pad") {
      value = std::sin(phase * 2.0 * M_PI) * .70 +
              std::sin(phase * 2.0 * 2.0 * M_PI) * .18 +
              std::sin(phase * 3.0 * 2.0 * M_PI) * .10;
    } else if (patch == "brass") {
      value = (1.0 - 2.0 * phase) * .42 + std::sin(phase * 2.0 * M_PI) * .48;
    } else if (patch == "flute" || patch == "reed") {
      value = std::sin(phase * 2.0 * M_PI) * .88 + std::sin(phase * 2.0 * 2.0 * M_PI) * .10;
    } else if (patch == "epiano") {
      value = std::sin(phase * 2.0 * M_PI) * .72 + std::sin(phase * 4.0 * 2.0 * M_PI) * .22;
    } else if (patch == "organ") {
      value = std::sin(phase * 2.0 * M_PI) * .60 +
              std::sin(phase * 2.0 * 2.0 * M_PI) * .25 +
              std::sin(phase * 4.0 * 2.0 * M_PI) * .12;
    } else if (patch == "soft_lead") {
      value = std::sin(phase * 2.0 * M_PI) * .90;
    } else if (patch == "pluck") {
      value = (phase < .5 ? .55 : -.55) + std::sin(phase * 2.0 * M_PI) * .15;
    } else {
      value = phase < .5 ? .68 : -.68;
    }
    value = std::clamp(value, -1.0, 1.0);
    sample->data16[i] = (short)std::lround(value * 28000.0);
  }
  return sample;
}

static int makeWave(DivEngine& engine, int voice, const std::string& patch) {
  auto* wave = new DivWavetable();
  wave->len = 32;
  wave->min = 0;
  wave->max = 31;
  for (int i = 0; i < wave->len; ++i) {
    double phase = (double)i / wave->len;
    double normalized;
    if (patch == "bass") normalized = 1.0 - phase;
    else if (patch == "bell") normalized = .5 + .32 * std::sin(phase * 2.0 * M_PI) + .16 * std::sin(phase * 3.0 * 2.0 * M_PI);
    else if (patch == "strings" || patch == "pad" || patch == "epiano") normalized = std::sin(phase * 2.0 * M_PI) * .5 + .5;
    else if (patch == "soft_lead" || patch == "flute" || patch == "reed") normalized = std::sin(phase * 2.0 * M_PI) * .45 + .5;
    else normalized = phase < .5 ? .85 : .15;
    wave->data[i] = bounded((int)std::lround(normalized * 31.0), 0, 31);
  }
  return engine.addWavePtr(wave);
}

static void configureStandardMacros(DivInstrument* instrument, const std::string& chip, const std::string& patch) {
  instrument->std.volMacro.open = true;
  instrument->std.volMacro.len = 1;
  instrument->std.volMacro.val[0] = patch == "hat" || patch == "kick" ? 11 : 15;
  instrument->std.waveMacro.open = true;
  instrument->std.waveMacro.len = 1;
  if (chip == "sms") {
    instrument->std.dutyMacro.open = true;
    instrument->std.dutyMacro.len = 1;
    // Sega PSG noise modes: 0/1 are short/long preset-frequency noise and
    // 2/3 tie noise frequency to tone channel 3. Keep normal percussion on
    // preset noise so a bass line on tone 3 cannot retune the drums.
    instrument->std.dutyMacro.val[0] = patch == "hat" ? 0 : isPercussionPatch(patch) ? 1 : 0;
    instrument->std.waveMacro.val[0] = 0;
  } else if (chip == "pokey") {
    instrument->std.dutyMacro.open = true;
    instrument->std.dutyMacro.len = 1;
    instrument->std.dutyMacro.val[0] = patch == "bass" ? 0x04 : 0x00;
    instrument->std.waveMacro.val[0] = isPercussionPatch(patch) ? 0x08 : patch == "bass" ? 0x0A : 0x00;
  } else if (chip == "atari2600") {
    instrument->std.waveMacro.val[0] = patch == "bass" ? 0x08 : isPercussionPatch(patch) ? 0x06 : 0x00;
  } else {
    instrument->std.waveMacro.val[0] = patch == "bass" ? 1 : isPercussionPatch(patch) ? 2 : 0;
  }
}

static DivSample* makeDpcmSample(const std::string& patch) {
  const int count = patch == "kick" ? 768 : 512;
  auto* sample = new DivSample();
  sample->name = "Homelynx NES DPCM " + patch;
  sample->depth = DIV_SAMPLE_DEPTH_1BIT_DPCM;
  sample->centerRate = patch == "kick" ? 4181 : 8363;
  sample->loop = false;
  if (!sample->init(count)) throw std::runtime_error("could not allocate NES DPCM sample");
  for (int i = 0; i < count; ++i) {
    double envelope = 1.0 - (double)i / count;
    bool bit = patch == "kick"
      ? std::sin((double)i / 24.0) * envelope > 0
      : (((i * 13) ^ (i >> 3)) & 1) != 0;
    if (bit) sample->dataDPCM[i >> 3] |= (unsigned char)(1U << (i & 7));
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
  int voices = chip == "snes" ? 8 : chip == "genesis" ? 10 : chip == "pce" ? 6 : chip == "nes" ? 5 : chip == "c64_6581" || chip == "c64_8580" ? 3 : chip == "zx_spectrum" ? 6 : chip == "atari2600" ? 2 : chip == "pcspeaker" ? 1 : 4;
  int instruments = voices;
  std::map<int, std::string> patches;
  std::map<int, std::string> voiceClasses;
  for (const auto& item : request.at("notes")) {
    patches[item.value("instrumentId", 0)] = item.value("instrument", "lead");
    voiceClasses[item.value("instrumentId", 0)] = item.value("voiceClass", "pulse");
  }
  for (const auto& item : request.at("notes")) instruments = std::max(instruments, item.value("instrumentId", 0) + 1);

  for (int instrumentId = 0; instrumentId < instruments; ++instrumentId) {
    const auto patch = patches.count(instrumentId) ? patches[instrumentId] : (instrumentId == 2 ? "bass" : instrumentId == 3 ? "drums" : "lead");
    const auto voiceClass = voiceClasses.count(instrumentId) ? voiceClasses[instrumentId] : "pulse";
    const auto instrumentType = instrumentTypeFor(chip, voiceClass);
    int sampleIndex = -1;
    int waveIndex = -1;
    if (chip == "snes") sampleIndex = engine.addSamplePtr(makeSample(instrumentId, patch));
    if (chip == "nes" && (patch == "kick" || patch == "snare")) sampleIndex = engine.addSamplePtr(makeDpcmSample(patch));
    if (chip == "gb" || chip == "gbc" || chip == "gameboy") waveIndex = makeWave(engine, instrumentId, patch);

    // InstrumentId is a catalog key, not a channel number. Let Furnace create
    // the correct null/default instrument from an explicit type instead of
    // incorrectly treating InstrumentId as refChan.
    int created = engine.addInstrument(-1, instrumentType);
    if (created < 0) throw std::runtime_error("could not create instrument");
    DivInstrument* instrument = engine.song.ins[created];
    instrument->type = instrumentType;
    instrument->name = "Homelynx " + patch + " " + std::to_string(instrumentId);

    if (chip == "gb" || chip == "gbc" || chip == "gameboy") {
      instrument->gb.envVol = 15;
      instrument->gb.envDir = 0;
      instrument->gb.envLen = patch == "pluck" || isPercussionPatch(patch) ? 1 : patch == "soft_lead" ? 3 : patch == "strings" || patch == "pad" ? 5 : 4;
      instrument->gb.soundLen = 64;
      instrument->gb.softEnv = patch == "soft_lead" || patch == "strings" || patch == "pad";
      instrument->gb.alwaysInit = true;
      // Every GB instrument gets a valid 32-sample wavetable. Pulse/noise
      // channels ignore this macro; the Wave channel no longer reads nullWave.
      instrument->std.waveMacro.open = true;
      instrument->std.waveMacro.len = 1;
      instrument->std.waveMacro.loop = 0;
      instrument->std.waveMacro.val[0] = waveIndex;
    } else if (chip == "genesis" && (voiceClass == "psg" || voiceClass == "noise")) {
      configureStandardMacros(instrument, "sms", patch);
    } else if (chip == "genesis") {
      instrument->fm.alg = patch == "epiano" || patch == "bell" ? 4 : patch == "brass" ? 1 : wave == "fm" ? (instrumentId % 3 == 0 ? 4 : 0) : 0;
      instrument->fm.fb = 4;
      instrument->fm.op[0].ar = 31; instrument->fm.op[0].dr = 8; instrument->fm.op[0].rr = 5; instrument->fm.op[0].tl = 18; instrument->fm.op[0].mult = 1;
      instrument->fm.op[1].ar = 31; instrument->fm.op[1].dr = 12; instrument->fm.op[1].rr = 6; instrument->fm.op[1].tl = 32; instrument->fm.op[1].mult = 2;
      instrument->fm.op[2].ar = 31; instrument->fm.op[2].dr = 10; instrument->fm.op[2].rr = 5; instrument->fm.op[2].tl = 42; instrument->fm.op[2].mult = 1;
      instrument->fm.op[3].ar = 31; instrument->fm.op[3].dr = 8; instrument->fm.op[3].rr = 5; instrument->fm.op[3].tl = 8; instrument->fm.op[3].mult = 1;
    } else if (chip == "c64_6581" || chip == "c64_8580") {
      instrument->c64.triOn = patch == "bell" || patch == "strings" || patch == "flute" || patch == "reed" || wave == "triangle";
      instrument->c64.sawOn = patch == "bass" || patch == "brass" || patch == "organ" || patch == "pad" || wave == "saw";
      instrument->c64.pulseOn = patch == "lead" || patch == "soft_lead" || patch == "pluck" || patch == "epiano" || wave == "square" || wave == "fm";
      instrument->c64.noiseOn = isPercussionPatch(patch) || wave == "noise";
      if (!instrument->c64.triOn && !instrument->c64.sawOn && !instrument->c64.pulseOn && !instrument->c64.noiseOn)
        instrument->c64.pulseOn = true;
      int patchAttack = patch == "pad" || patch == "strings" ? 10 : patch == "brass" ? 3 : attack;
      int patchDecay = patch == "bell" || patch == "epiano" ? 12 : decay;
      int patchSustain = isPercussionPatch(patch) ? 0 : patch == "pad" || patch == "strings" ? 12 : sustain;
      int patchRelease = patch == "bell" || patch == "strings" || patch == "pad" ? 12 : release;
      instrument->c64.a = (unsigned char)bounded(patchAttack, 0, 15);
      instrument->c64.d = (unsigned char)bounded(patchDecay, 0, 15);
      instrument->c64.s = (unsigned char)bounded(patchSustain, 0, 15);
      instrument->c64.r = (unsigned char)bounded(patchRelease, 0, 15);
      instrument->c64.duty = (unsigned short)((patch == "soft_lead" ? 12 : duty) * 4095 / 100);
      instrument->c64.toFilter = patch == "bass" || patch == "pad" || patch == "strings";
      instrument->c64.lp = true;
      int requestedCutoff = bounded(request.value("filter", 0), 0, 2047);
      instrument->c64.cut = requestedCutoff > 0 ? requestedCutoff : patch == "bass" ? 650 : patch == "pad" ? 1200 : 900;
      instrument->c64.res = patch == "bell" || patch == "pad" ? 8 : 5;
    } else if (chip == "nes") {
      if (sampleIndex >= 0) {
        instrument->amiga.useSample = true;
        instrument->amiga.initSample = sampleIndex;
      }
    } else if (chip == "snes") {
      instrument->amiga.useSample = true;
      instrument->amiga.initSample = sampleIndex;
      instrument->snes.useEnv = true;
      // SNES ADSR fields have different hardware ranges: A 0..15, D 0..7,
      // S 0..7 and R 0..31. Generic command defaults are translated to
      // musical patch defaults instead of using A=0 (a ~4.1 s attack).
      int patchAttack = attack == 0
        ? (patch == "pad" ? 9 : patch == "strings" ? 11 : patch == "brass" ? 13 : 15)
        : bounded(attack, 0, 15);
      int patchDecay = decay == 8
        ? (patch == "bell" || patch == "epiano" ? 4 : 3)
        : bounded(decay, 0, 7);
      int patchSustain = sustain == 12
        ? (isPercussionPatch(patch) ? 0 : patch == "strings" || patch == "pad" ? 6 : patch == "bell" || patch == "epiano" ? 3 : 5)
        : bounded(sustain, 0, 7);
      int patchRelease = release == 8
        ? (isPercussionPatch(patch) ? 3 : patch == "strings" || patch == "pad" ? 12 : patch == "bell" ? 10 : 6)
        : bounded(release, 0, 31);
      instrument->snes.a = (unsigned char)patchAttack;
      instrument->snes.d = (unsigned char)patchDecay;
      instrument->snes.s = (unsigned char)patchSustain;
      instrument->snes.r = (unsigned char)patchRelease;
    } else if (chip == "pce") {
      int pceWave = makeWave(engine, instrumentId, patch);
      instrument->std.waveMacro.len = 1;
      instrument->std.waveMacro.val[0] = pceWave;
      instrument->std.waveMacro.loop = 0;
      instrument->std.waveMacro.open = true;
    } else if (chip == "pokey") {
      configureStandardMacros(instrument, chip, patch);
    } else if (chip == "pcspeaker" || chip == "zx_spectrum") {
      configureStandardMacros(instrument, chip, patch);
    } else if (chip == "atari2600") {
      configureStandardMacros(instrument, chip, patch);
    } else if (chip == "sms") {
      configureStandardMacros(instrument, chip, patch);
    }
  }
  if (chip == "snes") engine.renderSamples();
}

struct CompileStats {
  int notesReceived = 0;
  int notesWritten = 0;
  int startRowsAdjusted = 0;
  int noteOffsSuppressed = 0;
};

struct CompiledNote {
  int voice;
  int startRow;
  int endRow;
};

static CompileStats fillSong(DivEngine& engine, const json& request) {
  DivSubSong* sub = engine.song.subsong.at(0);
  int bpm = bounded(request.value("bpm", 140), 40, 300);
  long endTick = request.value("endTick", 960L);
  constexpr int maxRows = 255 * 256;
  int ticksPerRow = std::max(1, (int)std::ceil(endTick / (double)maxRows));
  sub->hz = (float)bpm * 16.0f / ticksPerRow;
  sub->speeds.len = 1;
  sub->speeds.val[0] = 1;
  int endRow = std::max(1, (int)std::ceil(endTick / (double)ticksPerRow));
  sub->patLen = endRow <= 256 ? bounded(endRow + 1, 2, 256) : 256;
  if (endRow > maxRows) throw std::runtime_error("MIDI is larger than Furnace tracker capacity (255 patterns x 256 rows).");
  sub->ordersLen = std::max(1, (endRow + sub->patLen - 1) / sub->patLen);
  const int rowCapacity = sub->ordersLen * sub->patLen;
  for (int channel = 0; channel < engine.song.chans; ++channel)
    for (int order = 0; order < sub->ordersLen; ++order)
      sub->orders.ord[channel][order] = (unsigned char)order;

  CompileStats stats;
  std::vector<CompiledNote> compiled;
  std::map<int, int> lastStartRow;
  const auto& notes = request.at("notes");

  for (const auto& item : notes) {
    stats.notesReceived++;
    int voice = item.value("voice", 0);
    if (voice < 0 || voice >= engine.song.chans)
      throw std::runtime_error("arranger produced an invalid hardware voice");
    long startTick = item.value("startTick", 0L);
    long durationTick = std::max(1L, item.value("durationTick", 240L));
    int startRow = std::max(0, (int)std::llround(startTick / (double)ticksPerRow));
    auto previousRow = lastStartRow.find(voice);
    if (previousRow != lastStartRow.end() && startRow <= previousRow->second) {
      startRow = previousRow->second + 1;
      stats.startRowsAdjusted++;
    }
    if (startRow >= rowCapacity)
      throw std::runtime_error("tracker row capacity exhausted while preserving note onsets");
    lastStartRow[voice] = startRow;
    int endNoteRow = std::max(startRow + 1, (int)std::llround((startTick + durationTick) / (double)ticksPerRow));
    endNoteRow = std::min(endNoteRow, rowCapacity);

    int order = startRow / sub->patLen, row = startRow % sub->patLen;
    DivPattern* pattern = sub->pat[voice].getPattern(order, true);
    if (pattern->newData[row][DIV_PAT_NOTE] != -1)
      throw std::runtime_error("tracker note-on collision after row allocation");
    pattern->newData[row][DIV_PAT_NOTE] = (short)bounded(item.value("pitch", 60) + 48, 0, 179);
    int instrumentId = item.value("instrumentId", voice);
    if (instrumentId < 0 || instrumentId >= 180)
      throw std::runtime_error("instrument catalog index exceeds Furnace capacity");
    pattern->newData[row][DIV_PAT_INS] = (short)instrumentId;
    int maxVolume = engine.getMaxVolumeChan(voice);
    int velocity = bounded(item.value("velocity", 100), 1, 127);
    int expression = bounded(item.value("expression", 127), 0, 127);
    int volume = bounded(item.value("volume", 127), 0, 127);
    int effectiveVolume = (velocity * expression * volume + 8064) / (127 * 127);
    pattern->newData[row][DIV_PAT_VOL] = (short)bounded((effectiveVolume * maxVolume + 63) / 127, 1, maxVolume);

    int pan = bounded(item.value("pan", 64), 0, 127);
    int nextEffect = 0;
    if (pan != 64) {
      int left = bounded((127 - pan) * 15 / 127, 0, 15);
      int right = bounded(pan * 15 / 127, 0, 15);
      pattern->newData[row][DIV_PAT_FX(0)] = 0x88;
      pattern->newData[row][DIV_PAT_FXVAL(0)] = (short)((left << 4) | right);
      nextEffect = 1;
    }
    int modulation = bounded(item.value("modulation", 0), 0, 127);
    int vibrato = bounded(std::max(request.value("vibrato", 0), modulation * 31 / 127), 0, 31);
    if (vibrato > 0 && nextEffect < DIV_MAX_EFFECTS) {
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = 0xE4;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)vibrato;
      nextEffect++;
    }
    int pitchSlide = bounded(item.value("pitchSlide", 0), -127, 127);
    if (pitchSlide != 0 && nextEffect < DIV_MAX_EFFECTS) {
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = pitchSlide > 0 ? 0x01 : 0x02;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)bounded(std::abs(pitchSlide), 1, 255);
      nextEffect++;
    }
    int volumeSlide = bounded(item.value("volumeSlide", 0), -127, 127);
    if (volumeSlide != 0 && nextEffect < DIV_MAX_EFFECTS) {
      int amount = bounded(std::abs(volumeSlide), 1, 15);
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = 0x0A;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)(volumeSlide > 0 ? amount << 4 : amount);
      nextEffect++;
    }
    int retrigger = bounded(item.value("retrigger", 0), 0, 255);
    if (retrigger > 0 && nextEffect < DIV_MAX_EFFECTS) {
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = 0x0C;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)retrigger;
      nextEffect++;
    }
    auto ticksToRows = [ticksPerRow](long ticks) {
      return bounded((int)std::llround(ticks / (double)ticksPerRow), 1, 255);
    };
    int noteDelay = bounded(item.value("noteDelayTicks", 0), 0, 255 * ticksPerRow);
    if (noteDelay > 0 && nextEffect < DIV_MAX_EFFECTS) {
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = 0xED;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)ticksToRows(noteDelay);
      nextEffect++;
    }
    int noteCut = item.value("noteCutTicks", -1);
    if (noteCut >= 0 && nextEffect < DIV_MAX_EFFECTS) {
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = 0xEC;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)ticksToRows(noteCut);
      nextEffect++;
    }
    double bendSemitones = (item.value("pitchBend", 8192) - 8192) / 8192.0 * item.value("pitchBendRange", 2);
    double residual = bendSemitones - std::round(bendSemitones);
    if (std::abs(residual) >= 0.01 && nextEffect < DIV_MAX_EFFECTS) {
      pattern->newData[row][DIV_PAT_FX(nextEffect)] = 0xE5;
      pattern->newData[row][DIV_PAT_FXVAL(nextEffect)] = (short)bounded((int)std::lround(128.0 + residual * 128.0), 0, 255);
    }

    compiled.push_back({voice, startRow, endNoteRow});
    stats.notesWritten++;
  }

  for (int voice = 0; voice < engine.song.chans; ++voice) {
    std::vector<CompiledNote> lane;
    for (const auto& note : compiled) if (note.voice == voice) lane.push_back(note);
    std::sort(lane.begin(), lane.end(), [](const CompiledNote& a, const CompiledNote& b) { return a.startRow < b.startRow; });
    for (size_t i = 0; i < lane.size(); ++i) {
      const auto& current = lane[i];
      int nextStart = i + 1 < lane.size() ? lane[i + 1].startRow : rowCapacity + 1;
      if (nextStart <= current.endRow) {
        stats.noteOffsSuppressed++;
        continue;
      }
      if (current.endRow >= rowCapacity) continue;
      int offOrder = current.endRow / sub->patLen, offRow = current.endRow % sub->patLen;
      DivPattern* offPattern = sub->pat[voice].getPattern(offOrder, true);
      if (offPattern->newData[offRow][DIV_PAT_NOTE] == -1)
        offPattern->newData[offRow][DIV_PAT_NOTE] = DIV_NOTE_OFF;
      else
        stats.noteOffsSuppressed++;
    }
  }

  for (const auto& item : notes) {
    int voice = item.value("voice", 0);
    if (voice < 0 || voice >= engine.song.chans) continue;
    long noteStart = item.value("startTick", 0L);
    long noteEnd = noteStart + std::max(1L, item.value("durationTick", 240L));
    int bendRange = bounded(item.value("pitchBendRange", 2), 0, 24);
    for (const auto& bend : item.value("pitchBends", json::array())) {
      long tick = bend.value("tick", noteStart);
      if (tick <= noteStart || tick >= noteEnd) continue;
      int rowIndex = std::max(0, (int)std::llround(tick / (double)ticksPerRow));
      if (rowIndex >= rowCapacity) continue;
      int order = rowIndex / sub->patLen, row = rowIndex % sub->patLen;
      DivPattern* pattern = sub->pat[voice].getPattern(order, true);
      int effect = -1;
      for (int slot = 0; slot < DIV_MAX_EFFECTS; ++slot) {
        if (pattern->newData[row][DIV_PAT_FX(slot)] == -1) { effect = slot; break; }
      }
      if (effect < 0) continue;
      double semitones = (bend.value("value", 8192) - 8192) / 8192.0 * bendRange;
      double residualPoint = semitones - std::round(semitones);
      pattern->newData[row][DIV_PAT_FX(effect)] = 0xE5;
      pattern->newData[row][DIV_PAT_FXVAL(effect)] = (short)bounded((int)std::lround(128.0 + residualPoint * 128.0), 0, 255);
    }
  }

  for (const auto& item : notes) {
    int voice = item.value("voice", 0);
    if (voice < 0 || voice >= engine.song.chans) continue;
    long noteStart = item.value("startTick", 0L);
    long noteEnd = noteStart + std::max(1L, item.value("durationTick", 240L));
    int velocity = bounded(item.value("velocity", 100), 1, 127);
    for (const auto& point : item.value("controllerChanges", json::array())) {
      long tick = point.value("tick", noteStart);
      if (tick <= noteStart || tick >= noteEnd) continue;
      int rowIndex = std::max(0, (int)std::llround(tick / (double)ticksPerRow));
      if (rowIndex >= rowCapacity) continue;
      int order = rowIndex / sub->patLen, row = rowIndex % sub->patLen;
      DivPattern* pattern = sub->pat[voice].getPattern(order, true);
      int volume = bounded(point.value("volume", 127), 0, 127);
      int expression = bounded(point.value("expression", 127), 0, 127);
      int effectiveVolume = (velocity * expression * volume + 8064) / (127 * 127);
      int maxVolume = engine.getMaxVolumeChan(voice);
      pattern->newData[row][DIV_PAT_VOL] = (short)bounded((effectiveVolume * maxVolume + 63) / 127, 0, maxVolume);
      int effect = -1;
      for (int slot = 0; slot < DIV_MAX_EFFECTS; ++slot) {
        if (pattern->newData[row][DIV_PAT_FX(slot)] == -1) { effect = slot; break; }
      }
      int pan = bounded(point.value("pan", 64), 0, 127);
      if (pan != 64 && effect >= 0) {
        int left = bounded((127 - pan) * 15 / 127, 0, 15);
        int right = bounded(pan * 15 / 127, 0, 15);
        pattern->newData[row][DIV_PAT_FX(effect)] = 0x88;
        pattern->newData[row][DIV_PAT_FXVAL(effect)] = (short)((left << 4) | right);
        effect++;
      }
      int modulation = bounded(std::max(point.value("modulation", 0), point.value("aftertouch", 0)), 0, 127);
      if (modulation > 0 && effect >= 0 && effect < DIV_MAX_EFFECTS) {
        pattern->newData[row][DIV_PAT_FX(effect)] = 0xE4;
        pattern->newData[row][DIV_PAT_FXVAL(effect)] = (short)(modulation * 31 / 127);
      }
    }
  }

  engine.song.name = "Homelynx chiptune";
  engine.song.author = "Homelynx";
  engine.calcSongTimestamps();
  engine.syncReset();
  return stats;
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
    if (chip == "genesis" && !engine.addSystem(DIV_SYSTEM_SMS))
      throw std::runtime_error("could not add Genesis SN76489 PSG");
    configureInstruments(engine, chip, request);
    auto stats = fillSong(engine, request);
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
    std::cout << "{\"success\":true,\"backend\":\"furnace\",\"notesReceived\":" << stats.notesReceived
              << ",\"notesWritten\":" << stats.notesWritten
              << ",\"startRowsAdjusted\":" << stats.startRowsAdjusted
              << ",\"noteOffsSuppressed\":" << stats.noteOffsSuppressed << "}\n" << std::flush;
    std::_Exit(0);
  } catch (const std::exception& ex) {
    std::cerr << ex.what() << "\n" << std::flush;
    std::_Exit(1);
  }
}
