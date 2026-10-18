"""
Makes the stem splitter's model: Demucs' Hybrid Transformer model (HTDemucs, by Meta AI, MIT licence)
as an ONNX file the audio editor runs with ONNX Runtime, on the graphics card where it can.

ONNX can't hold HTDemucs' complex-valued STFT, so the model is exported without it: Lucent does the
STFT and inverse STFT itself (src/Lucent.Core/Editor/Stems/StemSplitter.cs), and the ONNX model takes
the music and its spectrogram and gives back each stem's spectrogram and waveform. This script also
holds a NumPy copy of Lucent's side of the work, and checks that the two together come out the same as
Demucs' own `apply_model`, so a mistake in either shows up here.

The weights are stored as 16-bit floats to halve the download; ONNX Runtime turns them back into
32-bit floats when it loads the model, so the sums are still done in full precision.

    python export_htdemucs.py htdemucs.onnx            # the real model (downloads Demucs' weights)
    python export_htdemucs.py test.onnx --random       # untrained, to check the export without the download
"""

import argparse
import math
import sys
import time

import numpy as np
import onnx
import onnxruntime as ort
import torch
from onnx import TensorProto, helper, numpy_helper

from demucs.apply import apply_model


class Core(torch.nn.Module):
    """HTDemucs.forward between the STFT and the inverse STFT (copied from demucs/htdemucs.py, 4.0.1)."""

    def __init__(self, model):
        super().__init__()
        self.model = model

    def forward(self, mix, spec):
        m = self.model
        length = mix.shape[-1]
        x = spec
        B, C, Fq, T = x.shape

        mean = x.mean(dim=(1, 2, 3), keepdim=True)
        std = x.std(dim=(1, 2, 3), keepdim=True)
        x = (x - mean) / (1e-5 + std)

        xt = mix
        meant = xt.mean(dim=(1, 2), keepdim=True)
        stdt = xt.std(dim=(1, 2), keepdim=True)
        xt = (xt - meant) / (1e-5 + stdt)

        saved = []
        saved_t = []
        lengths = []
        lengths_t = []
        for idx, encode in enumerate(m.encoder):
            lengths.append(x.shape[-1])
            inject = None
            if idx < len(m.tencoder):
                lengths_t.append(xt.shape[-1])
                tenc = m.tencoder[idx]
                xt = tenc(xt)
                if not tenc.empty:
                    saved_t.append(xt)
                else:
                    inject = xt
            x = encode(x, inject)
            if idx == 0 and m.freq_emb is not None:
                frs = torch.arange(x.shape[-2], device=x.device)
                emb = m.freq_emb(frs).t()[None, :, :, None].expand_as(x)
                x = x + m.freq_emb_scale * emb
            saved.append(x)

        if m.crosstransformer:
            if m.bottom_channels:
                b, c, f, t = x.shape
                x = x.reshape(b, c, f * t)
                x = m.channel_upsampler(x)
                x = x.reshape(b, -1, f, t)
                xt = m.channel_upsampler_t(xt)
            x, xt = m.crosstransformer(x, xt)
            if m.bottom_channels:
                b, c, f, t = x.shape
                x = x.reshape(b, c, f * t)
                x = m.channel_downsampler(x)
                x = x.reshape(b, -1, f, t)
                xt = m.channel_downsampler_t(xt)

        for idx, decode in enumerate(m.decoder):
            skip = saved.pop(-1)
            x, pre = decode(x, skip, lengths.pop(-1))
            offset = m.depth - len(m.tdecoder)
            if idx >= offset:
                tdec = m.tdecoder[idx - offset]
                length_t = lengths_t.pop(-1)
                if tdec.empty:
                    pre = pre[:, :, 0]
                    xt, _ = tdec(pre, None, length_t)
                else:
                    skip = saved_t.pop(-1)
                    xt, _ = tdec(xt, skip, length_t)

        S = len(m.sources)
        x = x.view(B, S, -1, Fq, T)
        x = x * std[:, None] + mean[:, None]
        xt = xt.view(B, S, -1, length)
        xt = xt * stdt[:, None] + meant[:, None]
        return x, xt


# ---- Lucent's side of the work, in NumPy (StemSplitter.cs does the same) ----

NFFT = 4096
HOP = NFFT // 4


def window():
    n = np.arange(NFFT)
    return (0.5 - 0.5 * np.cos(2 * np.pi * n / NFFT)).astype(np.float64)  # periodic Hann, like torch.hann_window


def spectrogram(x):
    """HTDemucs._spec, with its complex numbers as pairs of channels: [channels, samples] -> [channels * 2, 2048, frames]."""
    length = x.shape[-1]
    le = math.ceil(length / HOP)
    pad = HOP // 2 * 3
    x = np.pad(x, ((0, 0), (pad, pad + le * HOP - length)), mode="reflect")
    x = np.pad(x, ((0, 0), (NFFT // 2, NFFT // 2)), mode="reflect")
    frames = 1 + (x.shape[-1] - NFFT) // HOP
    w = window()
    out = np.zeros((x.shape[0] * 2, NFFT // 2, le), dtype=np.float32)
    for c in range(x.shape[0]):
        for f in range(2, 2 + le):
            z = np.fft.rfft(x[c, f * HOP: f * HOP + NFFT] * w) / math.sqrt(NFFT)
            out[c * 2, :, f - 2] = z.real[:-1]
            out[c * 2 + 1, :, f - 2] = z.imag[:-1]
    assert frames == le + 4
    return out


def inverse_spectrogram(spec, length):
    """HTDemucs._ispec: [channels * 2, 2048, frames] -> [channels, length]."""
    channels = spec.shape[0] // 2
    frames = spec.shape[-1]
    pad = HOP // 2 * 3
    le = HOP * math.ceil(length / HOP) + 2 * pad
    w = window()
    total = NFFT + HOP * (frames + 4 - 1)
    out = np.zeros((channels, total))
    envelope = np.zeros(total)
    for f in range(frames + 4):
        envelope[f * HOP: f * HOP + NFFT] += w * w
    for c in range(channels):
        for f in range(frames):
            z = np.zeros(NFFT // 2 + 1, dtype=np.complex128)
            z[:-1] = spec[c * 2, :, f] + 1j * spec[c * 2 + 1, :, f]
            frame = np.fft.irfft(z, NFFT) * math.sqrt(NFFT)
            at = (f + 2) * HOP
            out[c, at: at + NFFT] += frame * w
    out = out[:, NFFT // 2: NFFT // 2 + le] / envelope[NFFT // 2: NFFT // 2 + le]
    return out[:, pad: pad + length]


def separate(session, mix, segment_length):
    """Demucs' apply_model (no shifts, 25% overlap) over ONNX: [2, samples] -> [stems, 2, samples]."""
    ref = mix.mean(0)
    mean, std = ref.mean(), ref.std(ddof=1)
    mix = (mix - mean) / std
    length = mix.shape[-1]
    stride = int(0.75 * segment_length)
    weight = np.concatenate([np.arange(1, segment_length // 2 + 1),
                             np.arange(segment_length - segment_length // 2, 0, -1)]).astype(np.float64)
    weight /= weight.max()
    out = None
    total = np.zeros(length)
    for offset in range(0, length, stride):
        chunk = min(segment_length, length - offset)
        start = offset - (segment_length - chunk) // 2
        padded = np.zeros((2, segment_length), dtype=np.float32)
        lo, hi = max(0, start), min(length, start + segment_length)
        padded[:, lo - start: hi - start] = mix[:, lo:hi]
        spec_out, wave_out = session.run(None, {"mix": padded[None], "spec": spectrogram(padded)[None]})
        stems = np.stack([inverse_spectrogram(spec_out[0, s], segment_length) + wave_out[0, s]
                          for s in range(spec_out.shape[1])])
        trim = (segment_length - chunk) // 2
        stems = stems[..., trim: trim + chunk]
        if out is None:
            out = np.zeros((stems.shape[0], 2, length))
        out[..., offset: offset + chunk] += weight[:chunk] * stems
        total[offset: offset + chunk] += weight[:chunk]
    return out / total * std + mean


# ---- Export ----

def store_weights_as_half(model):
    """Swaps each large float initializer for a 16-bit copy and a Cast back, which ONNX Runtime folds when loading."""
    graph = model.graph
    casts = []
    kept = []
    for init in graph.initializer:
        if init.data_type == TensorProto.FLOAT and np.prod(init.dims) >= 1024:
            values = numpy_helper.to_array(init).astype(np.float16)
            half = numpy_helper.from_array(values, init.name + "_half")
            kept.append(half)
            casts.append(helper.make_node("Cast", [half.name], [init.name], to=TensorProto.FLOAT))
        else:
            kept.append(init)
    del graph.initializer[:]
    graph.initializer.extend(kept)
    nodes = list(graph.node)
    del graph.node[:]
    graph.node.extend(casts + nodes)
    return model


def load(random):
    if random:
        from fractions import Fraction
        from demucs.htdemucs import HTDemucs
        torch.manual_seed(0)
        model = HTDemucs(sources=["drums", "bass", "other", "vocals"], segment=Fraction(39, 5), bottom_channels=512)
        # Random weights give near-silent stems; louder layer scales give the check something to compare.
        for p in model.parameters():
            if p.dim() == 1:
                p.data.normal_(0, 0.5)
        return model
    from demucs.pretrained import get_model
    return get_model("htdemucs").models[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("output")
    parser.add_argument("--random", action="store_true", help="an untrained model, to check the export offline")
    parser.add_argument("--seconds", type=float, default=20, help="how much test music to check with")
    parser.add_argument("--save-check", action="store_true",
                        help="also save the test music and its stems as .npy files, to check Lucent's C# against")
    args = parser.parse_args()

    model = load(args.random).eval()
    segment_length = int(model.segment * model.samplerate)
    print(f"Sources {model.sources}, {model.samplerate} Hz, segment {segment_length} samples")
    core = Core(model).eval()
    mix = torch.zeros(1, 2, segment_length)
    spec = torch.zeros(1, 4, NFFT // 2, math.ceil(segment_length / HOP))
    # PyTorch's fused attention has no ONNX form; the ordinary one does the same sums.
    torch.backends.mha.set_fastpath_enabled(False)
    with torch.no_grad():
        torch.onnx.export(core, (mix, spec), args.output, input_names=["mix", "spec"],
                          output_names=["spec_out", "wave_out"], opset_version=17, dynamo=False)

    exported = onnx.load(args.output)
    store_weights_as_half(exported)
    for key, value in {
        "lucent.model": "htdemucs",
        "lucent.sources": ",".join(model.sources),
        "lucent.sample_rate": str(model.samplerate),
        "lucent.segment_length": str(segment_length),
        "lucent.licence": "Demucs (c) Meta Platforms, Inc. and affiliates, MIT licence: https://github.com/facebookresearch/demucs",
    }.items():
        entry = exported.metadata_props.add()
        entry.key, entry.value = key, value
    onnx.checker.check_model(exported)
    onnx.save(exported, args.output)

    # Music-like test audio: a few notes, a kick and some noise, so every stem has something to find.
    rng = np.random.default_rng(1)
    rate = model.samplerate
    t = np.arange(int(args.seconds * rate)) / rate
    test = 0.3 * np.sin(2 * np.pi * 110 * t) * (np.sin(2 * np.pi * 0.5 * t) > 0)
    test += 0.2 * np.sin(2 * np.pi * 440 * t + 3 * np.sin(2 * np.pi * 5 * t))
    test += 0.5 * np.exp(-30 * (t % 0.5)) * np.sin(2 * np.pi * 60 * t)
    test = np.stack([test + 0.05 * rng.standard_normal(t.size), test * 0.8 + 0.05 * rng.standard_normal(t.size)])
    test = test.astype(np.float32)

    began = time.time()
    with torch.no_grad():
        mixture = torch.from_numpy(test)
        ref = mixture.mean(0)
        expected = apply_model(model, ((mixture - ref.mean()) / ref.std())[None], shifts=0, split=True, overlap=0.25)[0]
        expected = (expected * ref.std() + ref.mean()).numpy()
    print(f"Demucs took {time.time() - began:.1f} s")

    began = time.time()
    session = ort.InferenceSession(args.output, providers=["CPUExecutionProvider"])
    got = separate(session, test, segment_length)
    print(f"ONNX took {time.time() - began:.1f} s")

    for s, name in enumerate(model.sources):
        error = np.abs(got[s] - expected[s]).max()
        level = np.abs(expected[s]).max()
        print(f"{name}: peak {level:.4f}, largest difference {error:.6f}")
        # The 16-bit weights shift the result a little; anything near the stem's own level is a real mistake.
        if not error <= 0.01 * max(level, 0.01):
            sys.exit(f"The ONNX model's {name} doesn't match Demucs' own")
    print(f"Saved {args.output}")
    if args.save_check:
        np.save(args.output + ".test-input.npy", test)
        np.save(args.output + ".test-output.npy", got.astype(np.float32))


if __name__ == "__main__":
    main()
