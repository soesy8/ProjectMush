import json, math, os, struct, wave
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

SRC = Path(r"C:\Users\MBC\Downloads\SE\SE")
OUT = Path(r"D:\project\ProjectMush\.codex_tmp\se_review")
OUT.mkdir(parents=True, exist_ok=True)


def db(x):
    return 20 * math.log10(max(float(x), 1e-12))


def read_wav(path):
    with wave.open(str(path), "rb") as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if sw == 2:
        x = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768
    elif sw == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3)
        v = b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16)
        v = np.where(v & 0x800000, v - 0x1000000, v)
        x = v.astype(np.float32) / 8388608
    elif sw == 4:
        x = np.frombuffer(raw, dtype="<i4").astype(np.float32) / 2147483648
    else:
        raise ValueError(sw)
    multi = x.reshape(-1, ch) if ch > 1 else x.reshape(-1, 1)
    mono = multi.mean(axis=1)
    return mono, multi, sr, ch, sw


def analyze(path):
    x, multi, sr, ch, sw = read_wav(path)
    peak = np.max(np.abs(x))
    rms = np.sqrt(np.mean(x*x))
    # 20 ms RMS envelope
    hop = max(1, int(sr * .02))
    usable = len(x) // hop * hop
    env = np.sqrt(np.mean(x[:usable].reshape(-1, hop) ** 2, axis=1) + 1e-16)
    env_db = 20*np.log10(env + 1e-12)
    active = env_db > -48
    # Spectral summaries from 2048-point frames, max 300 frames.
    nfft = 2048
    starts = np.linspace(0, max(0, len(x)-nfft), min(300, max(1, len(x)//1024))).astype(int)
    win = np.hanning(nfft)
    specs = np.array([np.abs(np.fft.rfft(x[s:s+nfft] * win))**2 for s in starts])
    mean_spec = specs.mean(axis=0) + 1e-20
    freqs = np.fft.rfftfreq(nfft, 1/sr)
    centroid = float(np.sum(freqs*mean_spec)/np.sum(mean_spec))
    csum = np.cumsum(mean_spec); rolloff = float(freqs[np.searchsorted(csum, csum[-1]*.85)])
    bands = [(20,120),(120,500),(500,2000),(2000,6000),(6000,16000),(16000,24000)]
    band_pct = {}
    total = mean_spec[(freqs>=20)&(freqs<=24000)].sum()
    for lo,hi in bands:
        band_pct[f"{lo}-{hi}"] = round(float(mean_spec[(freqs>=lo)&(freqs<hi)].sum()/total*100),1)
    # Envelope peaks: distinct events above adaptive threshold with 100 ms separation.
    threshold = max(np.percentile(env, 65), 10**(-36/20))
    peaks = []
    gap = 5
    for i in range(1, len(env)-1):
        if env[i] > threshold and env[i] >= env[i-1] and env[i] > env[i+1] and (not peaks or i-peaks[-1] >= gap):
            peaks.append(i)
    intervals = np.diff(peaks)*.02 if len(peaks)>1 else np.array([])
    if ch > 1:
        corr = float(np.corrcoef(multi[:,0], multi[:,1])[0,1])
        mid = (multi[:,0] + multi[:,1])*.5
        side = (multi[:,0] - multi[:,1])*.5
        width_db = db(np.sqrt(np.mean(side*side))/np.sqrt(np.mean(mid*mid)))
    else:
        corr, width_db = 1.0, -120.0
    clipped = float(np.mean(np.abs(multi) >= .9999)*100)
    edge_n=max(1,int(sr*.05))
    loop_edge_delta=float(np.sqrt(np.mean((multi[:edge_n]-multi[-edge_n:])**2)))
    return {
        "file": path.name, "duration_s": round(len(x)/sr,3), "sample_rate": sr,
        "channels": ch, "bit_depth": sw*8, "peak_dbfs": round(db(peak),1),
        "rms_dbfs": round(db(rms),1), "crest_db": round(db(peak/rms),1),
        "stereo_correlation": round(corr,3), "side_to_mid_db": round(width_db,1),
        "samples_near_clip_pct": round(clipped,5), "loop_edge_delta_db": round(db(loop_edge_delta),1),
        "active_pct_above_-48db": round(float(active.mean()*100),1),
        "env_p10_db": round(float(np.percentile(env_db,10)),1),
        "env_p90_db": round(float(np.percentile(env_db,90)),1),
        "spectral_centroid_hz": round(centroid), "rolloff85_hz": round(rolloff),
        "band_energy_pct": band_pct, "event_peaks": len(peaks),
        "median_peak_interval_s": round(float(np.median(intervals)),2) if len(intervals) else None,
        "x": x, "sr": sr,
    }


results=[]
for p in sorted(SRC.glob("*.wav")):
    results.append(analyze(p))

serial=[]
for r in results:
    serial.append({k:v for k,v in r.items() if k not in ("x","sr")})
(OUT/"analysis.json").write_text(json.dumps(serial, indent=2), encoding="utf-8")

# Contact sheet: waveform + log-frequency spectrogram for each file.
W, row_h = 1500, 190
canvas = Image.new("RGB", (W, row_h*len(results)), (245,245,242))
draw = ImageDraw.Draw(canvas)
font = ImageFont.load_default()
for ri,r in enumerate(results):
    y0=ri*row_h; x=r["x"]; sr=r["sr"]
    draw.text((10,y0+7), f'{r["file"]}  {r["duration_s"]:.1f}s  RMS {r["rms_dbfs"]} dBFS  peak {r["peak_dbfs"]} dBFS  centroid {r["spectral_centroid_hz"]} Hz', fill=(20,25,35), font=font)
    # waveform
    left, right, top, bot=10, 1490, y0+26, y0+82
    bins=right-left
    idx=np.linspace(0,len(x),bins+1).astype(int)
    mx=np.array([np.max(np.abs(x[idx[i]:idx[i+1]])) if idx[i+1]>idx[i] else 0 for i in range(bins)])
    mid=(top+bot)//2
    for i,a in enumerate(mx):
        h=int(a*(bot-top)/2/.98); draw.line((left+i,mid-h,left+i,mid+h),fill=(36,75,110))
    # spectrogram, 0-12k mapped vertically
    nfft=1024; cols=740; starts=np.linspace(0,max(0,len(x)-nfft),cols).astype(int); win=np.hanning(nfft)
    S=np.array([np.abs(np.fft.rfft(x[s:s+nfft]*win)) for s in starts]).T
    S=20*np.log10(S+1e-7); S=np.clip((S+75)/75,0,1)
    maxbin=min(S.shape[0], int(12000/(sr/nfft)))
    S=S[:maxbin][::-1]
    im=np.zeros((80,cols,3),dtype=np.uint8)
    # resize via PIL after a simple blue-orange mapping
    rgb=np.stack([np.clip(S*1.5,0,1), np.clip((S-.18)*1.25,0,1), np.clip((1-S)*.55,0,1)],axis=2)
    spec=Image.fromarray((rgb*255).astype(np.uint8)).resize((1480,80))
    canvas.paste(spec,(10,y0+98))
canvas.save(OUT/"wave_spectrogram_contactsheet.png")
print(OUT/"analysis.json")
print(OUT/"wave_spectrogram_contactsheet.png")
