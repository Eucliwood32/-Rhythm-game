#!/usr/bin/env python3
"""
채보 편집기용 외부 분석기.

곡을 분석해서 채보 편집기의 "외부 분석 불러오기"가 읽는 JSON을 만든다.
편집기 안의 분석보다 무겁지만 더 정확하다. 특히 --demucs를 쓰면 드럼만 따로 뽑아서 분석하므로
기타가 꽉 찬 메탈 곡에서 킥·스네어를 훨씬 잘 잡는다.

설치 (한 번만):
    pip install librosa soundfile
    pip install demucs      # 선택: 음원 분리 (처음 실행 때 모델을 내려받는다. 곡 하나에 CPU로 1~3분)
    pip install madmom      # 선택: 마디 첫 박을 더 정확하게

사용:
    python chart_analyze.py 곡.wav                 # 곡.analysis.json 생성
    python chart_analyze.py 곡.mp3 --demucs        # 음원 분리 후 분석 (추천)
    python chart_analyze.py 곡.flac -o 결과.json

만든 JSON을 Unity 프로젝트 안(예: Assets/Charts)에 넣고, 채보 편집기 > 자동 생성 > "외부 분석 불러오기"로 고른다.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile

import numpy as np

try:
    import librosa
except ImportError:
    sys.exit("librosa가 없습니다. 먼저 'pip install librosa soundfile'을 실행하세요.")

SR = 44100
HOP = 512
N_FFT = 2048


def log(msg):
    print("[chart_analyze] " + msg, flush=True)


# ───────────────────────── 음원 분리 ─────────────────────────

def separate(path):
    """demucs로 drums / bass / other / vocals를 뽑는다. 실패하면 None."""
    if shutil.which("demucs") is None:
        try:
            import demucs  # noqa: F401
        except ImportError:
            log("demucs가 설치되어 있지 않아 음원 분리 없이 분석합니다. (pip install demucs)")
            return None
    out = tempfile.mkdtemp(prefix="chart_demucs_")
    log("음원 분리 중 (demucs, 몇 분 걸릴 수 있습니다)...")
    cmd = [sys.executable, "-m", "demucs", "-n", "htdemucs", "-o", out, path]
    try:
        subprocess.run(cmd, check=True)
    except (subprocess.CalledProcessError, OSError) as e:
        log("demucs 실패: %s. 음원 분리 없이 분석합니다." % e)
        return None
    name = os.path.splitext(os.path.basename(path))[0]
    folder = os.path.join(out, "htdemucs", name)
    stems = {}
    for s in ("drums", "bass", "other", "vocals"):
        f = os.path.join(folder, s + ".wav")
        if os.path.exists(f):
            stems[s], _ = librosa.load(f, sr=SR, mono=True)
    return stems if "drums" in stems else None


# ───────────────────────── 타격 지점 ─────────────────────────

def drum_onsets(drums):
    """드럼 신호에서 타격 지점을 찾고, 에너지가 몰린 대역으로 kick / snare / hihat을 가린다."""
    S = np.abs(librosa.stft(drums, n_fft=N_FFT, hop_length=HOP))
    mel = librosa.feature.melspectrogram(S=S ** 2, sr=SR, n_mels=128, fmax=16000)
    mel_f = librosa.mel_frequencies(n_mels=128, fmax=16000)
    # 대역 비교는 멜 대역(저음은 촘촘, 고음은 성기게) 크기로 한다. FFT 칸 그대로 더하면 칸이 많은 고음이 늘 이긴다.
    B = np.sqrt(mel)
    freqs = mel_f
    low = freqs < 150
    mid = (freqs >= 150) & (freqs < 3500)
    high = freqs > 5000

    # 실제 크기의 증가량 (앞 2프레임, 이웃 주파수 최대값과 비교 = SuperFlux의 선형판)
    prev = np.roll(B, 2, axis=1)
    prev[:, :2] = B[:, :1]
    prev = np.maximum(prev, np.maximum(np.roll(prev, 1, axis=0), np.roll(prev, -1, axis=0)))
    d = np.maximum(0, B - prev)
    lin = {k: d[m].sum(axis=0) for k, m in (("low", low), ("mid", mid), ("high", high))}
    # 2~3프레임에 걸쳐 퍼지는 타격을 합쳐서 본다
    for k in lin:
        lin[k] = np.convolve(lin[k], np.ones(4), mode="same")
    total = lin["low"] + lin["mid"] + lin["high"] + 1e-12
    low_share = lin["low"] / total
    high_share = lin["high"] / total

    env = librosa.onset.onset_strength(S=librosa.power_to_db(mel), sr=SR, hop_length=HOP, lag=2, max_size=3)
    env_low = librosa.onset.onset_strength(S=librosa.power_to_db(mel[mel_f < 150]), sr=SR, hop_length=HOP, lag=2, max_size=3)

    def norm(e):
        r = np.percentile(e, 99)
        return np.clip(e / r, 0, 1) if r > 0 else e * 0

    env_n, low_n = norm(env), norm(env_low)
    mid_n = norm(lin["mid"])
    high_n = norm(lin["high"])
    lowv_n = norm(lin["low"])

    onsets = []
    frames = librosa.onset.onset_detect(onset_envelope=env, sr=SR, hop_length=HOP, units="frames", backtrack=False)
    for f in frames:
        t = float(librosa.frames_to_time(f, sr=SR, hop_length=HOP))
        ls, hs = float(low_share[f]), float(high_share[f])
        s = float(env_n[f])
        if ls >= 0.65:
            onsets.append({"t": t, "kind": "kick", "s": max(s, float(lowv_n[f]))})
        elif hs >= 0.45:
            onsets.append({"t": t, "kind": "hihat", "s": max(s * 0.8, float(high_n[f]))})
        elif ls >= 0.3:   # 킥 + 스네어가 같이 친 타격
            onsets.append({"t": t, "kind": "kick", "s": float(lowv_n[f])})
            onsets.append({"t": t, "kind": "snare", "s": float(mid_n[f])})
        else:
            onsets.append({"t": t, "kind": "snare", "s": max(s, float(mid_n[f]))})

    # 다른 소리에 묻힌 킥: 저음 곡선에서 따로 찾는다
    have = np.array([o["t"] for o in onsets if o["kind"] == "kick"])
    for f in librosa.onset.onset_detect(onset_envelope=env_low, sr=SR, hop_length=HOP, units="frames", backtrack=False):
        t = float(librosa.frames_to_time(f, sr=SR, hop_length=HOP))
        if have.size and np.min(np.abs(have - t)) < 0.035:
            continue
        if low_share[f] >= 0.3 or low_n[f] >= 0.8:
            onsets.append({"t": t, "kind": "kick", "s": float(low_n[f])})

    onsets.sort(key=lambda o: o["t"])
    return onsets, env_n + 0.5 * low_n, low_n


# ───────────────────────── 박자 ─────────────────────────

def beats_and_downbeats(y, env, low_env, path):
    """박과 마디 첫 박. madmom이 있으면 그걸 쓰고, 없으면 librosa + 킥 위치로 마디 첫 박을 고른다."""
    try:
        from madmom.features.downbeats import RNNDownBeatProcessor, DBNDownBeatTrackingProcessor
        log("마디 첫 박: madmom")
        act = RNNDownBeatProcessor()(path)
        res = DBNDownBeatTrackingProcessor(beats_per_bar=[4], fps=100)(act)
        beats = res[:, 0].astype(float)
        downbeats = beats[res[:, 1] == 1]
        return beats, downbeats
    except Exception:
        pass

    tempo, frames = librosa.beat.beat_track(onset_envelope=env, sr=SR, hop_length=HOP, start_bpm=140, tightness=100)
    beats = librosa.frames_to_time(frames, sr=SR, hop_length=HOP)
    # 절반 박자로 잡았는지: 박 사이(반 박)에도 박만큼 센 타격이 꾸준히 있으면 두 배로
    if len(frames) > 8:
        mids = ((frames[:-1] + frames[1:]) // 2).astype(int)
        on = env[frames].mean()
        off = env[mids].mean()
        if off >= 0.6 * on and 60.0 / np.median(np.diff(beats)) * 2 <= 240:
            tempo2, frames2 = librosa.beat.beat_track(onset_envelope=env, sr=SR, hop_length=HOP,
                                                      start_bpm=float(np.atleast_1d(tempo)[0]) * 2, tightness=100)
            if len(frames2) > len(frames) * 1.5:
                frames = frames2
                beats = librosa.frames_to_time(frames, sr=SR, hop_length=HOP)
    # 마디 첫 박: 4가지 위상 중 킥이 가장 많이 모이는 위상
    best, phase = -1, 0
    for k in range(4):
        sc = low_env[frames[k::4]].sum() if len(frames) > k else 0
        if sc > best:
            best, phase = sc, k
    return beats, beats[phase::4]


# ───────────────────────── 지속음 ─────────────────────────

def sustains(harm):
    """새로 시작해서 길게 이어지는 소리(롱노트 후보)."""
    rms = librosa.feature.rms(y=harm, frame_length=N_FFT, hop_length=HOP)[0]
    rms = np.convolve(rms, np.ones(5) / 5, mode="same")
    ref = np.percentile(rms, 99) or 1e-9
    env = librosa.onset.onset_strength(y=harm, sr=SR, hop_length=HOP)
    out = []
    w = int(round(0.05 * SR / HOP))
    for f in librosa.onset.onset_detect(onset_envelope=env, sr=SR, hop_length=HOP, units="frames"):
        before = rms[max(0, f - w)]
        peak = rms[f:f + w + 1].max() if f < len(rms) else 0
        if peak < 0.3 * ref or peak - before < 0.05 * ref:
            continue
        j = f + w
        while j < len(rms) and rms[j] >= 0.6 * peak:
            j += 1
        start = float(librosa.frames_to_time(f, sr=SR, hop_length=HOP))
        end = float(librosa.frames_to_time(j, sr=SR, hop_length=HOP))
        if end - start >= 0.3:
            out.append({"start": start, "end": end, "s": float(min(1.0, peak / ref))})
    return out


# ───────────────────────── 곡 구간 ─────────────────────────

def sections(y, beats, duration):
    """곡을 비슷한 부분끼리 묶어 구간으로 나누고, 구간마다 세기(0~1)를 매긴다."""
    if len(beats) < 16:
        return [{"start": 0.0, "end": duration, "intensity": 0.5}]
    frames = librosa.time_to_frames(beats, sr=SR, hop_length=HOP)
    chroma = librosa.feature.chroma_cqt(y=y, sr=SR, hop_length=HOP)
    mfcc = librosa.feature.mfcc(y=y, sr=SR, hop_length=HOP, n_mfcc=13)
    rms = librosa.feature.rms(y=y, hop_length=HOP)
    feat = np.vstack([librosa.util.normalize(chroma), librosa.util.normalize(mfcc), rms / (rms.max() + 1e-9)])
    sync = librosa.util.sync(feat, frames, aggregate=np.median)
    k = int(np.clip(round(duration / 20), 3, 12))
    k = min(k, sync.shape[1] - 1)
    bound_idx = librosa.segment.agglomerative(sync, k)
    bt = np.concatenate([[0.0], beats])
    times = sorted(set([0.0] + [float(bt[min(i, len(bt) - 1)]) for i in bound_idx if i > 0] + [duration]))
    rms_full = librosa.feature.rms(y=y, hop_length=HOP)[0]
    raw = []
    for a, b in zip(times[:-1], times[1:]):
        fa, fb = librosa.time_to_frames([a, b], sr=SR, hop_length=HOP)
        raw.append(float(rms_full[fa:max(fa + 1, fb)].mean()))
    raw = np.array(raw)
    mm = (raw - raw.min()) / (raw.max() - raw.min() + 1e-12)
    rank = np.argsort(np.argsort(raw)) / max(1, len(raw) - 1)
    return [{"start": a, "end": b, "intensity": float(0.5 * m + 0.5 * r)}
            for a, b, m, r in zip(times[:-1], times[1:], mm, rank)]


# ───────────────────────── 실행 ─────────────────────────

def main():
    ap = argparse.ArgumentParser(description="채보 편집기용 외부 분석기")
    ap.add_argument("audio")
    ap.add_argument("-o", "--output")
    ap.add_argument("--demucs", action="store_true", help="음원 분리(demucs) 후 분석")
    args = ap.parse_args()

    path = args.audio
    out = args.output or os.path.splitext(path)[0] + ".analysis.json"
    log("불러오는 중: " + path)
    y, _ = librosa.load(path, sr=SR, mono=True)
    duration = len(y) / SR

    stems = separate(path) if args.demucs else None
    if stems:
        drums = stems["drums"]
        harm = sum(stems[s] for s in ("vocals", "other", "bass") if s in stems)
        source = "librosa + demucs"
    else:
        harm, drums = librosa.effects.hpss(y)
        source = "librosa"

    log("타격 지점 분석...")
    onsets, env, low_env = drum_onsets(drums)
    log("박자 분석...")
    beats, downbeats = beats_and_downbeats(y, env, low_env, path)
    # BPM = 박 시각들을 직선으로 맞춘 기울기 (프레임 단위로 끊긴 박 간격의 중앙값보다 정확하다)
    bpm = float(60.0 / np.polyfit(np.arange(len(beats)), beats, 1)[0]) if len(beats) > 1 else 0.0
    log("지속음 분석...")
    sus = sustains(harm)
    log("곡 구간 분석...")
    secs = sections(y, beats, duration)

    data = {
        "version": 1,
        "source": source,
        "duration": duration,
        "bpm": round(bpm, 2),
        "beats": [round(float(b), 4) for b in beats],
        "downbeats": [round(float(b), 4) for b in downbeats],
        "onsets": [{"t": round(o["t"], 4), "kind": o["kind"], "s": round(float(o["s"]), 3)} for o in onsets],
        "sustains": [{"start": round(s["start"], 4), "end": round(s["end"], 4), "s": round(s["s"], 3)} for s in sus],
        "sections": [{"start": round(s["start"], 3), "end": round(s["end"], 3), "intensity": round(s["intensity"], 3)} for s in secs],
    }
    with open(out, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False)
    log("완료: %s (BPM %.2f, 타격 %d개, 지속음 %d개, 구간 %d개)" % (out, bpm, len(onsets), len(sus), len(secs)))


if __name__ == "__main__":
    main()
