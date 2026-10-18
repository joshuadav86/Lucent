# The stem splitter's model

The audio editor's **Stems** tool splits music into vocals, drums, bass and the other instruments with
[Demucs](https://github.com/facebookresearch/demucs)' Hybrid Transformer model (HTDemucs), by Meta AI. Demucs'
code and its trained models are released under the MIT licence, so Lucent can use and share them. The licence
(`LICENSE-Demucs.txt`) is published beside the model and named in the model file's metadata.

`export_htdemucs.py` turns the trained model into an ONNX file, which Lucent runs with ONNX Runtime: on the
graphics card through DirectML on Windows (NVIDIA, AMD or Intel) and Core ML on a Mac, or on the processor.
ONNX can't hold the model's complex-valued STFT, so Lucent does the STFT itself
(`src/Lucent.Core/Editor/Stems/StemSplitter.cs`); the script checks a NumPy copy of that code, run with the
exported model, against Demucs' own output, and fails if they differ.

CI runs the script (the `stem-model` job in `.github/workflows/build.yml`), tests the model in the Windows and
Mac self-tests, and publishes it once to the `stem-model-v1` release, where the app downloads it the first time
someone uses Stems. To change the model, change the script and bump `StemModel.Version`, so copies of Lucent
already installed keep the model they were built for.

To run it yourself (Python 3.11):

    pip install -r tools/StemModel/requirements.txt
    python tools/StemModel/export_htdemucs.py htdemucs.onnx

`--random` exports an untrained model of the same shape, to try the export without downloading the weights.
Point `LUCENT_STEM_MODEL` at a model file to have Lucent use it instead of downloading one.
