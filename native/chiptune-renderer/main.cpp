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
#include <initializer_list>
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

static bool isNoisePercussionPatch(const std::string& patch) {
  return patch == "drums" || patch == "snare" || patch == "hat" || patch == "open_hat" ||
         patch == "crash" || patch == "ride";
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

static void setSequence(DivInstrumentMacro& macro, std::initializer_list<int> values, int speed=1, int loop=-1) {
  macro.open = true;
  macro.len = (unsigned char)std::min<size_t>(values.size(), 255);
  macro.delay = 0;
  macro.speed = (unsigned char)bounded(speed, 1, 255);
  macro.loop = loop >= 0 && loop < macro.len ? (unsigned char)loop : 255;
  macro.rel = 255;
  int index = 0;
  for (int value : values) {
    if (index >= macro.len) break;
    macro.val[index++] = value;
  }
}

static void configureVolumeShape(DivInstrument* instrument, const std::string& patch, int maxVolume) {
  maxVolume = std::max(1, maxVolume);
  if (maxVolume == 1) {
    if (isPercussionPatch(patch) || patch == "pluck" || patch == "bell")
      setSequence(instrument->std.volMacro, {1, 1, 0}, patch == "bell" ? 2 : 1);
    else
      setSequence(instrument->std.volMacro, {1});
    return;
  }

  if (patch == "kick")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume, maxVolume*3/4, maxVolume/2, maxVolume/4, 0});
  else if (patch == "snare" || patch == "tom")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume*3/4, maxVolume/2, maxVolume/3, maxVolume/6, 0});
  else if (patch == "hat")
    setSequence(instrument->std.volMacro, {maxVolume*3/4, maxVolume/2, maxVolume/4, 0});
  else if (patch == "open_hat" || patch == "crash" || patch == "ride" || patch == "drums")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume*7/8, maxVolume*3/4, maxVolume*5/8, maxVolume/2, maxVolume/3, maxVolume/5, 0}, 2);
  else if (patch == "pluck")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume*7/8, maxVolume*2/3, maxVolume/2, maxVolume/3, maxVolume/4});
  else if (patch == "bell")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume*7/8, maxVolume*3/4, maxVolume*5/8, maxVolume/2, maxVolume*3/8, maxVolume/4}, 2);
  else if (patch == "epiano")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume*7/8, maxVolume*3/4, maxVolume*2/3}, 2);
  else if (patch == "soft_lead")
    setSequence(instrument->std.volMacro, {maxVolume*3/4, maxVolume, maxVolume*7/8}, 2);
  else if (patch == "strings" || patch == "pad")
    setSequence(instrument->std.volMacro, {maxVolume/3, maxVolume/2, maxVolume*2/3, maxVolume*5/6, maxVolume, maxVolume*7/8}, 2);
  else if (patch == "brass")
    setSequence(instrument->std.volMacro, {maxVolume*2/3, maxVolume, maxVolume*7/8});
  else if (patch == "bass")
    setSequence(instrument->std.volMacro, {maxVolume, maxVolume*7/8, maxVolume*3/4}, 2);
  else
    setSequence(instrument->std.volMacro, {maxVolume});
}

static int snesSampleLength(const std::string& patch) {
  if (patch == "kick") return 1024;
  if (patch == "snare" || patch == "drums") return 1536;
  if (patch == "hat") return 512;
  if (patch == "open_hat") return 3072;
  if (patch == "tom") return 1280;
  if (patch == "crash" || patch == "ride") return 4096;
  return 64;
}

static DivSample* makeSample(int voice, const std::string& patch) {
  const bool percussion = isPercussionPatch(patch);
  const int count = snesSampleLength(patch);
  DivSample* sample = new DivSample();
  sample->name = "Homelynx SNES patch " + patch;
  sample->depth = DIV_SAMPLE_DEPTH_16BIT;
  sample->centerRate = 16744;
  sample->loop = !percussion;
  sample->loopStart = 0;
  sample->loopEnd = count;
  sample->dither = true;
  sample->brrNoFilter = false;
  if (!sample->init(count)) throw std::runtime_error("could not allocate SNES sample");
  uint32_t noise = 0x7fffU ^ (uint32_t)(voice * 977);
  for (int i = 0; i < count; ++i) {
    double phase = (double)i / count;
    double value;
    if (patch == "kick") {
      double envelope = std::pow(1.0 - phase, 3.0);
      double cycles = 7.0 * phase - 4.8 * phase * phase;
      value = std::sin(cycles * 2.0 * M_PI) * envelope;
    } else if (patch == "tom") {
      double envelope = std::pow(1.0 - phase, 2.4);
      double cycles = 10.0 * phase - 3.0 * phase * phase;
      value = std::sin(cycles * 2.0 * M_PI) * envelope;
    } else if (percussion) {
      uint32_t bit = ((noise >> 0) ^ (noise >> 1)) & 1U;
      noise = (noise >> 1) | (bit << 14);
      double decayPower = patch == "open_hat" || patch == "crash" || patch == "ride" ? 1.5 : 2.7;
      double envelope = std::pow(1.0 - phase, decayPower);
      double tonal = patch == "ride" ? std::sin(phase * 37.0 * 2.0 * M_PI) * .16 : 0.0;
      value = ((noise & 1U) ? .62 : -.62) * envelope + tonal * envelope;
    } else if (patch == "bass") {
      value = (1.0 - 2.0 * phase) * .58 + std::sin(phase * 2.0 * M_PI) * .32 + std::sin(phase * 2.0 * 2.0 * M_PI) * .08;
    } else if (patch == "bell") {
      value = std::sin(phase * 2.0 * M_PI) * .55 +
              std::sin(phase * 3.0 * 2.0 * M_PI) * .23 +
              std::sin(phase * 5.0 * 2.0 * M_PI) * .14 +
              std::sin(phase * 7.0 * 2.0 * M_PI) * .08;
    } else if (patch == "strings" || patch == "pad") {
      value = std::sin(phase * 2.0 * M_PI) * .65 +
              std::sin(phase * 2.0 * 2.0 * M_PI) * .19 +
              std::sin(phase * 3.0 * 2.0 * M_PI) * .10 +
              std::sin(phase * 4.0 * 2.0 * M_PI) * .05;
    } else if (patch == "brass") {
      value = (1.0 - 2.0 * phase) * .38 +
              std::sin(phase * 2.0 * M_PI) * .42 +
              std::sin(phase * 2.0 * 2.0 * M_PI) * .15;
    } else if (patch == "flute" || patch == "reed") {
      value = std::sin(phase * 2.0 * M_PI) * .88 + std::sin(phase * 2.0 * 2.0 * M_PI) * .10;
    } else if (patch == "epiano") {
      value = std::sin(phase * 2.0 * M_PI) * .70 + std::sin(phase * 4.0 * 2.0 * M_PI) * .20 + std::sin(phase * 6.0 * 2.0 * M_PI) * .08;
    } else if (patch == "organ") {
      value = std::sin(phase * 2.0 * M_PI) * .58 +
              std::sin(phase * 2.0 * 2.0 * M_PI) * .25 +
              std::sin(phase * 4.0 * 2.0 * M_PI) * .13;
    } else if (patch == "soft_lead") {
      value = std::sin(phase * 2.0 * M_PI) * .90 + std::sin(phase * 2.0 * 2.0 * M_PI) * .07;
    } else if (patch == "pluck") {
      value = (phase < .5 ? .52 : -.52) + std::sin(phase * 2.0 * M_PI) * .18;
    } else {
      value = phase < .5 ? .66 : -.66;
    }
    // Furnace's standalone build currently compiles this adapter as C++14;
    // keep the renderer compatible instead of relying on std::clamp (C++17).
    value = bounded(value, -1.0, 1.0);
    sample->data16[i] = (short)std::lround(value * 28000.0);
  }
  return sample;
}

static int makeWave(DivEngine& engine, const std::string& patch) {
  auto* wave = new DivWavetable();
  wave->len = 32;
  wave->min = 0;
  wave->max = 31;
  for (int i = 0; i < wave->len; ++i) {
    double phase = (double)i / wave->len;
    double normalized;
    if (patch == "kick") normalized = .5 + .45 * std::sin(phase * 2.0 * M_PI);
    else if (patch == "tom") normalized = .5 + .42 * std::sin(phase * 2.0 * M_PI) + .06 * std::sin(phase * 4.0 * M_PI);
    else if (patch == "bass") normalized = .50 + .34 * (1.0 - 2.0 * phase) + .14 * std::sin(phase * 2.0 * M_PI);
    else if (patch == "bell") normalized = .5 + .28 * std::sin(phase * 2.0 * M_PI) + .15 * std::sin(phase * 3.0 * 2.0 * M_PI) + .07 * std::sin(phase * 5.0 * 2.0 * M_PI);
    else if (patch == "strings" || patch == "pad" || patch == "epiano") normalized = .5 + .38 * std::sin(phase * 2.0 * M_PI) + .08 * std::sin(phase * 2.0 * 2.0 * M_PI);
    else if (patch == "soft_lead" || patch == "flute" || patch == "reed") normalized = std::sin(phase * 2.0 * M_PI) * .43 + .5;
    else if (patch == "brass") normalized = .5 + .28 * (1.0 - 2.0 * phase) + .18 * std::sin(phase * 2.0 * M_PI);
    else normalized = phase < .5 ? .84 : .16;
    wave->data[i] = bounded((int)std::lround(normalized * 31.0), 0, 31);
  }
  return engine.addWavePtr(wave);
}

static void configureStandardMacros(DivInstrument* instrument, const std::string& chip, const std::string& patch, const std::string& voiceClass) {
  int maxVolume = chip == "pce" ? 31 : (chip == "pcspeaker" || chip == "zx_spectrum" ? 1 : 15);
  configureVolumeShape(instrument, patch, maxVolume);

  if (chip == "sms") {
    if (voiceClass == "noise") {
      int noiseMode = patch == "hat" ? 0 : 1;
      setSequence(instrument->std.dutyMacro, {noiseMode});
    }
  } else if (chip == "pce") {
    if (isNoisePercussionPatch(patch)) setSequence(instrument->std.dutyMacro, {1});
  } else if (chip == "pokey") {
    int waveform = 5;
    if (patch == "kick" || patch == "tom") waveform = 6;
    else if (patch == "bell") waveform = 1;
    else if (patch == "reed" || patch == "flute") waveform = 7;
    else if (isPercussionPatch(patch)) waveform = patch == "hat" ? 0 : 4;
    setSequence(instrument->std.waveMacro, {waveform});
  } else if (chip == "atari2600") {
    int waveform = 4;
    if (patch == "bass" || patch == "kick" || patch == "tom") waveform = 0x0c;
    else if (patch == "reed" || patch == "flute" || patch == "bell") waveform = 7;
    else if (isPercussionPatch(patch)) waveform = 8;
    else if (patch == "soft_lead" || patch == "strings" || patch == "pad") waveform = 5;
    setSequence(instrument->std.waveMacro, {waveform});
  }
}

static void setFmOperator(DivInstrumentFM::Operator& op, int ar, int dr, int d2r, int rr, int sl, int tl, int mult, int dt=0) {
  op.enable = true;
  op.ar = (unsigned char)bounded(ar, 0, 31);
  op.dr = (unsigned char)bounded(dr, 0, 31);
  op.d2r = (unsigned char)bounded(d2r, 0, 31);
  op.rr = (unsigned char)bounded(rr, 0, 15);
  op.sl = (unsigned char)bounded(sl, 0, 15);
  op.tl = (unsigned char)bounded(tl, 0, 127);
  op.mult = (unsigned char)bounded(mult, 0, 15);
  op.dt = (unsigned char)bounded(dt, 0, 7);
}

static void configureFmPatch(DivInstrument* instrument, const std::string& patch) {
  instrument->fm.ops = 4;
  instrument->fm.fms = 0;
  instrument->fm.ams = 0;

  if (patch == "kick") {
    instrument->fm.alg = 7; instrument->fm.fb = 3;
    setFmOperator(instrument->fm.op[0],31,24,24,15,15,12,1);
    setFmOperator(instrument->fm.op[1],31,26,24,15,15,34,1);
    setFmOperator(instrument->fm.op[2],31,25,24,15,15,42,2);
    setFmOperator(instrument->fm.op[3],31,22,22,15,15,4,1);
  } else if (patch == "tom") {
    instrument->fm.alg = 7; instrument->fm.fb = 2;
    setFmOperator(instrument->fm.op[0],31,18,18,13,15,16,1);
    setFmOperator(instrument->fm.op[1],31,20,18,13,15,38,2);
    setFmOperator(instrument->fm.op[2],31,20,18,13,15,44,3);
    setFmOperator(instrument->fm.op[3],31,17,16,12,15,5,1);
  } else if (patch == "epiano") {
    instrument->fm.alg = 4; instrument->fm.fb = 2;
    setFmOperator(instrument->fm.op[0],31,10,8,7,7,20,1);
    setFmOperator(instrument->fm.op[1],31,13,10,8,10,34,3);
    setFmOperator(instrument->fm.op[2],31,9,7,7,8,26,1);
    setFmOperator(instrument->fm.op[3],31,11,9,8,10,7,1);
  } else if (patch == "bell") {
    instrument->fm.alg = 4; instrument->fm.fb = 1;
    setFmOperator(instrument->fm.op[0],31,18,18,11,15,22,1);
    setFmOperator(instrument->fm.op[1],31,20,20,12,15,30,4,1);
    setFmOperator(instrument->fm.op[2],31,17,18,11,15,38,7,2);
    setFmOperator(instrument->fm.op[3],31,14,16,10,15,5,2);
  } else if (patch == "bass") {
    instrument->fm.alg = 0; instrument->fm.fb = 5;
    setFmOperator(instrument->fm.op[0],31,9,6,7,8,18,1);
    setFmOperator(instrument->fm.op[1],31,8,5,6,8,28,2);
    setFmOperator(instrument->fm.op[2],31,10,6,6,10,34,1);
    setFmOperator(instrument->fm.op[3],31,7,4,6,7,3,1);
  } else if (patch == "brass") {
    instrument->fm.alg = 1; instrument->fm.fb = 4;
    setFmOperator(instrument->fm.op[0],25,5,4,5,6,22,1);
    setFmOperator(instrument->fm.op[1],27,6,4,5,7,34,1);
    setFmOperator(instrument->fm.op[2],24,5,4,5,6,30,2);
    setFmOperator(instrument->fm.op[3],26,5,3,5,6,6,1);
  } else if (patch == "organ") {
    instrument->fm.alg = 7; instrument->fm.fb = 1;
    setFmOperator(instrument->fm.op[0],31,1,0,5,0,12,1);
    setFmOperator(instrument->fm.op[1],31,1,0,5,0,22,2);
    setFmOperator(instrument->fm.op[2],31,1,0,5,0,30,3);
    setFmOperator(instrument->fm.op[3],31,1,0,5,0,8,1);
  } else if (patch == "strings" || patch == "pad") {
    instrument->fm.alg = 7; instrument->fm.fb = 1;
    int fmAttack = patch == "pad" ? 11 : 16;
    setFmOperator(instrument->fm.op[0],fmAttack,4,2,5,4,18,1);
    setFmOperator(instrument->fm.op[1],fmAttack+2,4,2,5,4,28,2);
    setFmOperator(instrument->fm.op[2],fmAttack,4,2,5,4,36,3);
    setFmOperator(instrument->fm.op[3],fmAttack+2,4,2,5,4,10,1);
  } else if (patch == "pluck") {
    instrument->fm.alg = 4; instrument->fm.fb = 3;
    setFmOperator(instrument->fm.op[0],31,18,16,11,15,18,1);
    setFmOperator(instrument->fm.op[1],31,20,18,12,15,34,3);
    setFmOperator(instrument->fm.op[2],31,17,16,11,15,38,2);
    setFmOperator(instrument->fm.op[3],31,15,14,10,15,4,1);
  } else if (patch == "soft_lead" || patch == "reed" || patch == "flute") {
    instrument->fm.alg = 5; instrument->fm.fb = 2;
    setFmOperator(instrument->fm.op[0],25,6,4,6,7,24,1);
    setFmOperator(instrument->fm.op[1],27,7,4,6,7,36,2);
    setFmOperator(instrument->fm.op[2],25,6,4,6,7,40,1);
    setFmOperator(instrument->fm.op[3],28,6,4,6,7,5,1);
  } else {
    instrument->fm.alg = 0; instrument->fm.fb = 5;
    setFmOperator(instrument->fm.op[0],31,8,5,6,8,18,1);
    setFmOperator(instrument->fm.op[1],31,11,7,7,10,30,2);
    setFmOperator(instrument->fm.op[2],31,9,6,6,9,38,1);
    setFmOperator(instrument->fm.op[3],31,7,4,6,7,5,1);
  }
}

static DivSample* makeDpcmSample(const std::string& patch) {
  const int count = patch == "kick" ? 1024 : 768;
  auto* sample = new DivSample();
  sample->name = "Homelynx NES DPCM " + patch;
  sample->depth = DIV_SAMPLE_DEPTH_1BIT_DPCM;
  sample->centerRate = patch == "kick" ? 4181 : 8363;
  sample->loop = false;
  if (!sample->init(count)) throw std::runtime_error("could not allocate NES DPCM sample");
  for (int i = 0; i < count; ++i) {
    double phase = (double)i / count;
    bool bit;
    if (patch == "kick") {
      double envelope = std::pow(1.0 - phase, 2.8);
      bit = std::sin((phase * 8.0 - phase * phase * 4.0) * 2.0 * M_PI) * envelope > 0;
    } else {
      bit = (((i * 13) ^ (i >> 3) ^ (i * 7 >> 5)) & 1) != 0;
    }
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
  int instruments = 0;
  std::map<int, std::string> patches;
  std::map<int, std::string> voiceClasses;
  for (const auto& item : request.at("notes")) {
    int id = item.value("instrumentId", 0);
    patches[id] = item.value("instrument", "lead");
    voiceClasses[id] = item.value("voiceClass", "pulse");
    instruments = std::max(instruments, id + 1);
  }
  if (instruments < 1) instruments = 1;

  for (int instrumentId = 0; instrumentId < instruments; ++instrumentId) {
    const auto patch = patches.count(instrumentId) ? patches[instrumentId] : "lead";
    const auto voiceClass = voiceClasses.count(instrumentId) ? voiceClasses[instrumentId] : "pulse";
    const auto instrumentType = instrumentTypeFor(chip, voiceClass);
    int sampleIndex = -1;
    int waveIndex = -1;
    if (chip == "snes") sampleIndex = engine.addSamplePtr(makeSample(instrumentId, patch));
    if (chip == "nes" && voiceClass == "dpcm" && (patch == "kick" || patch == "snare"))
      sampleIndex = engine.addSamplePtr(makeDpcmSample(patch));
    if ((chip == "gb" || chip == "gbc" || chip == "gameboy") && voiceClass == "wave")
      waveIndex = makeWave(engine, patch);

    int created = engine.addInstrument(-1, instrumentType);
    if (created < 0) throw std::runtime_error("could not create instrument");
    if (created != instrumentId) throw std::runtime_error("Furnace instrument catalog index is not dense");
    DivInstrument* instrument = engine.song.ins[created];
    instrument->type = instrumentType;
    instrument->name = "Homelynx " + patch + " " + std::to_string(instrumentId);

    if (chip == "gb" || chip == "gbc" || chip == "gameboy") {
      instrument->gb.envVol = 15;
      instrument->gb.envDir = 0;
      instrument->gb.envLen = patch == "pluck" || patch == "kick" || patch == "hat" ? 1 : patch == "snare" ? 2 : patch == "bell" ? 2 : patch == "bass" ? 3 : 4;
      instrument->gb.soundLen = 64;
      instrument->gb.softEnv = voiceClass == "wave" || patch == "soft_lead" || patch == "strings" || patch == "pad";
      instrument->gb.alwaysInit = true;
      if (instrument->gb.softEnv) configureVolumeShape(instrument, patch, 15);
      if (voiceClass == "pulse") {
        int dutyMode = patch == "lead" || patch == "pluck" || patch == "epiano" ? 1 : patch == "brass" ? 3 : 2;
        setSequence(instrument->std.dutyMacro, {dutyMode});
      } else if (voiceClass == "noise") {
        setSequence(instrument->std.dutyMacro, {patch == "hat" || patch == "open_hat" ? 1 : 0});
      } else if (voiceClass == "wave" && waveIndex >= 0) {
        setSequence(instrument->std.waveMacro, {waveIndex});
      }
    } else if (chip == "genesis" && (voiceClass == "psg" || voiceClass == "noise")) {
      configureStandardMacros(instrument, "sms", patch, voiceClass);
    } else if (chip == "genesis") {
      configureFmPatch(instrument, patch);
    } else if (chip == "c64_6581" || chip == "c64_8580") {
      const bool percussion = isPercussionPatch(patch);
      const bool waveOverride = patch == "lead";
      if (percussion) {
        instrument->c64.triOn = false;
        instrument->c64.sawOn = false;
        instrument->c64.pulseOn = false;
        instrument->c64.noiseOn = true;
      } else {
        instrument->c64.triOn = patch == "bell" || patch == "strings" || patch == "flute" || patch == "reed" ||
                                (waveOverride && wave == "triangle");
        instrument->c64.sawOn = patch == "bass" || patch == "brass" || patch == "organ" || patch == "pad" ||
                                (waveOverride && wave == "saw");
        instrument->c64.pulseOn = patch == "lead" || patch == "soft_lead" || patch == "pluck" || patch == "epiano" ||
                                  patch == "organ" || patch == "strings" || patch == "pad" ||
                                  (waveOverride && (wave == "square" || wave == "fm"));
        instrument->c64.noiseOn = waveOverride && wave == "noise";
      }
      if (!instrument->c64.triOn && !instrument->c64.sawOn && !instrument->c64.pulseOn && !instrument->c64.noiseOn)
        instrument->c64.pulseOn = true;
      int patchAttack = patch == "pad" || patch == "strings" ? 10 : patch == "brass" ? 3 : attack;
      int patchDecay = patch == "bell" || patch == "epiano" ? 12 : decay;
      int patchSustain = percussion ? 0 : patch == "pad" || patch == "strings" ? 12 : sustain;
      int patchRelease = patch == "bell" || patch == "strings" || patch == "pad" ? 12 : release;
      instrument->c64.a = (unsigned char)bounded(patchAttack, 0, 15);
      instrument->c64.d = (unsigned char)bounded(patchDecay, 0, 15);
      instrument->c64.s = (unsigned char)bounded(patchSustain, 0, 15);
      instrument->c64.r = (unsigned char)bounded(patchRelease, 0, 15);
      instrument->c64.duty = (unsigned short)((patch == "soft_lead" ? 35 : duty) * 4095 / 100);
      instrument->c64.toFilter = !percussion && (patch == "bass" || patch == "pad" || patch == "strings" || patch == "brass");
      instrument->c64.initFilter = instrument->c64.toFilter;
      instrument->c64.lp = true;
      int requestedCutoff = bounded(request.value("filter", 0), 0, 2047);
      instrument->c64.cut = requestedCutoff > 0 ? requestedCutoff : patch == "bass" ? 600 : patch == "pad" ? 1150 : patch == "strings" ? 1350 : 900;
      instrument->c64.res = patch == "bell" || patch == "pad" ? 8 : patch == "bass" ? 6 : 5;

      if (instrument->c64.pulseOn && (patch == "lead" || patch == "soft_lead" || patch == "pluck" || patch == "strings" || patch == "pad")) {
        instrument->c64.dutyIsAbs = true;
        if (patch == "pluck") setSequence(instrument->std.dutyMacro, {900,1200,1600,1900});
        else if (patch == "soft_lead" || patch == "strings" || patch == "pad") setSequence(instrument->std.dutyMacro, {1200,1450,1750,2050,1750,1450}, 3, 0);
        else setSequence(instrument->std.dutyMacro, {900,1200,1500,1800,1500,1200}, 2, 0);
      }
      if (instrument->c64.toFilter && requestedCutoff == 0) {
        instrument->c64.filterIsAbs = true;
        if (patch == "bass") setSequence(instrument->std.algMacro, {480,560,680,820,720,600}, 2, 0);
        else if (patch == "pad") setSequence(instrument->std.algMacro, {750,900,1080,1260,1140,980}, 4, 0);
        else if (patch == "strings") setSequence(instrument->std.algMacro, {900,1080,1280,1450,1280,1080}, 3, 0);
      }
    } else if (chip == "nes") {
      if (voiceClass != "dpcm") configureVolumeShape(instrument, patch, 15);
      if (voiceClass == "pulse") {
        int dutyMode = patch == "lead" || patch == "pluck" || patch == "epiano" ? 1 : patch == "brass" ? 3 : 2;
        setSequence(instrument->std.dutyMacro, {dutyMode});
      } else if (voiceClass == "noise") {
        setSequence(instrument->std.dutyMacro, {patch == "hat" || patch == "open_hat" ? 1 : 0});
      } else if (voiceClass == "dpcm" && sampleIndex >= 0) {
        instrument->amiga.useSample = true;
        instrument->amiga.initSample = sampleIndex;
        instrument->amiga.useNoteMap = true;
        for (int i = 0; i < 180; ++i) {
          instrument->amiga.noteMap[i].map = (short)sampleIndex;
          instrument->amiga.noteMap[i].freq = i;
          instrument->amiga.noteMap[i].dpcmFreq = patch == "kick" ? 4 : 11;
          instrument->amiga.noteMap[i].dpcmDelta = -1;
        }
      }
    } else if (chip == "snes") {
      instrument->amiga.useSample = true;
      instrument->amiga.initSample = sampleIndex;
      if (isPercussionPatch(patch)) {
        instrument->amiga.useNoteMap = true;
        for (int i = 0; i < 180; ++i) {
          instrument->amiga.noteMap[i].map = (short)sampleIndex;
          instrument->amiga.noteMap[i].freq = 108;
        }
      }
      instrument->snes.useEnv = true;
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
      int pceWave = makeWave(engine, patch);
      setSequence(instrument->std.waveMacro, {pceWave});
      configureStandardMacros(instrument, chip, patch, voiceClass);
    } else if (chip == "pokey" || chip == "pcspeaker" || chip == "zx_spectrum" || chip == "atari2600" || chip == "sms") {
      configureStandardMacros(instrument, chip, patch, voiceClass);
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

static std::pair<int,int> panLevels(int pan) {
  pan = bounded(pan, 0, 127);
  if (pan <= 64) return {15, bounded((int)std::lround(pan / 64.0 * 15.0), 0, 15)};
  return {bounded((int)std::lround((127 - pan) / 63.0 * 15.0), 0, 15), 15};
}

static int appendEffect(DivPattern* pattern, int row, int effect, int value) {
  for (int slot = 0; slot < DIV_MAX_EFFECTS; ++slot) {
    if (pattern->newData[row][DIV_PAT_FX(slot)] != -1) continue;
    pattern->newData[row][DIV_PAT_FX(slot)] = (short)effect;
    pattern->newData[row][DIV_PAT_FXVAL(slot)] = (short)value;
    return slot;
  }
  return -1;
}

static void requireEffect(DivPattern* pattern, int row, int effect, int value, const char* name) {
  if (appendEffect(pattern, row, effect, value) < 0)
    throw std::runtime_error(std::string("tracker effect capacity exhausted while writing ") + name);
}

static void setStateEffect(DivPattern* pattern, int row, int effect, int value, const char* name) {
  for (int slot = 0; slot < DIV_MAX_EFFECTS; ++slot) {
    if (pattern->newData[row][DIV_PAT_FX(slot)] != effect) continue;
    pattern->newData[row][DIV_PAT_FXVAL(slot)] = (short)value;
    return;
  }
  requireEffect(pattern, row, effect, value, name);
}

static void writeVibrato(DivPattern* pattern, int row, int control) {
  control = bounded(control, 0, 31);
  if (control > 0) setStateEffect(pattern, row, 0xE4, control, "vibrato range");
  int depth = control == 0 ? 0 : bounded((control + 1) / 2, 1, 15);
  int speed = control == 0 ? 0 : 5;
  setStateEffect(pattern, row, 0x04, (speed << 4) | depth, "vibrato");
}

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
    pattern->newData[row][DIV_PAT_VOL] = (short)bounded((effectiveVolume * maxVolume + 63) / 127, 0, maxVolume);

    auto [leftPan,rightPan] = panLevels(item.value("pan", 64));
    setStateEffect(pattern, row, 0x08, (leftPan << 4) | rightPan, "panning");
    int modulation = bounded(item.value("modulation", 0), 0, 127);
    int explicitVibrato = bounded(request.value("vibrato", 0), 0, 31);
    int midiVibrato = modulation * 8 / 127;
    writeVibrato(pattern, row, std::max(explicitVibrato, midiVibrato));

    double bendSemitones = (item.value("pitchBend", 8192) - 8192) / 8192.0 * item.value("pitchBendRange", 2);
    double residual = bendSemitones - std::round(bendSemitones);
    setStateEffect(pattern, row, 0xE5, bounded((int)std::lround(128.0 + residual * 128.0), 0, 255), "fine pitch");

    int pitchSlide = bounded(item.value("pitchSlide", 0), -127, 127);
    if (pitchSlide != 0)
      appendEffect(pattern, row, pitchSlide > 0 ? 0x01 : 0x02, bounded(std::abs(pitchSlide), 1, 255));
    int volumeSlide = bounded(item.value("volumeSlide", 0), -127, 127);
    if (volumeSlide != 0) {
      int amount = bounded(std::abs(volumeSlide), 1, 15);
      appendEffect(pattern, row, 0x0A, volumeSlide > 0 ? amount << 4 : amount);
    }
    int retrigger = bounded(item.value("retrigger", 0), 0, 255);
    if (retrigger > 0) appendEffect(pattern, row, 0x0C, retrigger);

    auto ticksToRows = [ticksPerRow](long ticks) {
      return bounded((int)std::llround(ticks / (double)ticksPerRow), 1, 255);
    };
    int noteDelay = bounded(item.value("noteDelayTicks", 0), 0, 255 * ticksPerRow);
    if (noteDelay > 0) appendEffect(pattern, row, 0xED, ticksToRows(noteDelay));
    int noteCut = item.value("noteCutTicks", -1);
    if (noteCut >= 0) appendEffect(pattern, row, 0xEC, ticksToRows(noteCut));

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
    double initialSemitones = (item.value("pitchBend", 8192) - 8192) / 8192.0 * bendRange;
    int coarse = (int)std::lround(initialSemitones);
    std::map<int,int> bendByRow;
    for (const auto& bend : item.value("pitchBends", json::array())) {
      long tick = bend.value("tick", noteStart);
      if (tick <= noteStart || tick >= noteEnd) continue;
      int rowIndex = std::max(0, (int)std::llround(tick / (double)ticksPerRow));
      if (rowIndex >= rowCapacity) continue;
      bendByRow[rowIndex] = bend.value("value", 8192); // last MIDI state wins inside one tracker row
    }
    for (const auto& [rowIndex,bendValue] : bendByRow) {
      int order = rowIndex / sub->patLen, row = rowIndex % sub->patLen;
      DivPattern* pattern = sub->pat[voice].getPattern(order, true);
      double semitones = (bendValue - 8192) / 8192.0 * bendRange;
      int targetCoarse = (int)std::lround(semitones);
      int delta = targetCoarse - coarse;
      while (delta != 0) {
        int amount = std::min(std::abs(delta), 15);
        if (appendEffect(pattern, row, delta > 0 ? 0xE8 : 0xE9, amount) < 0) break;
        coarse += delta > 0 ? amount : -amount;
        delta = targetCoarse - coarse;
      }
      double residualPoint = semitones - targetCoarse;
      setStateEffect(pattern, row, 0xE5, bounded((int)std::lround(128.0 + residualPoint * 128.0), 0, 255), "pitch bend");
    }
  }

  for (const auto& item : notes) {
    int voice = item.value("voice", 0);
    if (voice < 0 || voice >= engine.song.chans) continue;
    long noteStart = item.value("startTick", 0L);
    long noteEnd = noteStart + std::max(1L, item.value("durationTick", 240L));
    int velocity = bounded(item.value("velocity", 100), 1, 127);
    std::map<int,json> controllerByRow;
    for (const auto& point : item.value("controllerChanges", json::array())) {
      long tick = point.value("tick", noteStart);
      if (tick <= noteStart || tick >= noteEnd) continue;
      int rowIndex = std::max(0, (int)std::llround(tick / (double)ticksPerRow));
      if (rowIndex >= rowCapacity) continue;
      controllerByRow[rowIndex] = point; // one representable state per row
    }
    for (const auto& [rowIndex,point] : controllerByRow) {
      int order = rowIndex / sub->patLen, row = rowIndex % sub->patLen;
      DivPattern* pattern = sub->pat[voice].getPattern(order, true);
      int volume = bounded(point.value("volume", 127), 0, 127);
      int expression = bounded(point.value("expression", 127), 0, 127);
      int effectiveVolume = (velocity * expression * volume + 8064) / (127 * 127);
      int maxVolume = engine.getMaxVolumeChan(voice);
      pattern->newData[row][DIV_PAT_VOL] = (short)bounded((effectiveVolume * maxVolume + 63) / 127, 0, maxVolume);
      auto [leftPan,rightPan] = panLevels(point.value("pan", 64));
      setStateEffect(pattern, row, 0x08, (leftPan << 4) | rightPan, "controller panning");
      int modulation = bounded(std::max(point.value("modulation", 0), point.value("aftertouch", 0)), 0, 127);
      writeVibrato(pattern, row, modulation * 8 / 127);
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
