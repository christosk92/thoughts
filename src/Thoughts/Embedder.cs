using System.Runtime.InteropServices;
using System.Text;
using Microsoft.ML.Tokenizers;

namespace Thoughts;

/// <summary>Runs <c>multilingual-e5-small</c> on this PC, on the NPU when there is one and the CPU otherwise. Nothing leaves
/// the PC. It binds ONNX Runtime's C API with function pointers, because that works under NativeAOT. A session is held only
/// while it's in use, since on the CPU it takes about 460 MB.</summary>
sealed unsafe class Embedder : IDisposable
{
    public const int Dimensions = 384;
    const int NpuLength = 128; // The NPU needs one fixed input length, and each length is its own slow compile.
    const uint OrtApiVersion = 30;
    const int HardwareNpu = 2;
    const int TensorInt64 = 7;

    // Positions in ONNX Runtime's OrtApi table for API version 30.
    const int ErrMsg = 2, CreateEnv = 3, CreateSession = 7, RunSession = 9, CreateSessionOptions = 10, CreateTensor = 49, GetTensorData = 51,
        CreateCpuMemoryInfo = 69, ReleaseStatus = 93, ReleaseMemoryInfo = 94, ReleaseSession = 95, ReleaseValue = 96, ReleaseSessionOptions = 100,
        AddFreeDimensionOverride = 124, AddConfigEntry = 130, RegisterExecutionProviderLibrary = 301, GetEpDevices = 303,
        AppendExecutionProviderV2 = 304, HardwareDeviceType = 307, EpDeviceName = 312, EpDeviceHardware = 316;

    static readonly object Gate = new();
    static void** s_api;
    static nint s_env;
    static nint s_npu;
    static bool s_runtimeTried;

    readonly nint _session;
    readonly nint _memory;
    readonly SentencePieceTokenizer _tokenizer;
    readonly bool _onNpu;

    public string Device => _onNpu ? "npu" : "cpu";

    Embedder(nint session, nint memory, SentencePieceTokenizer tokenizer, bool onNpu)
    {
        _session = session;
        _memory = memory;
        _tokenizer = tokenizer;
        _onNpu = onNpu;
    }

    public static bool IsInstalled(string dir) =>
        File.Exists(Path.Combine(dir, "runtime", "onnxruntime.dll"))
        && File.Exists(Path.Combine(dir, "e5-small", "model.onnx"))
        && File.Exists(Path.Combine(dir, "e5-small", "sentencepiece.bpe.model"));

    /// <summary>Null means it can't run here, and the app works just as well without it.</summary>
    public static Embedder? TryLoad(string dir, bool forceCpu)
    {
        if (!IsInstalled(dir)) return null;
        try
        {
            lock (Gate)
            {
                LoadRuntime(dir);
                if (s_api is null) return null;
                nint memory;
                Check(((delegate* unmanaged<int, int, nint*, nint>)s_api[CreateCpuMemoryInfo])(0, 0, &memory));
                SentencePieceTokenizer tokenizer;
                using (var file = File.OpenRead(Path.Combine(dir, "e5-small", "sentencepiece.bpe.model")))
                    tokenizer = SentencePieceTokenizer.Create(file, addBeginningOfSentence: false, addEndOfSentence: false);

                if (!forceCpu && s_npu != 0)
                {
                    try
                    {
                        return new Embedder(OpenOnNpu(dir), memory, tokenizer, true);
                    }
                    catch (InvalidOperationException)
                    {
                        // The NPU can refuse at any time, after a driver update say. The CPU's vectors match it closely enough to share an index.
                    }
                }
                return new Embedder(OpenOnCpu(dir), memory, tokenizer, false);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or IOException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return null;
        }
    }

    static void LoadRuntime(string dir)
    {
        if (s_runtimeTried) return;
        s_runtimeTried = true;
        string runtime = Path.Combine(dir, "runtime");
        nint library = NativeLibrary.Load(Path.Combine(runtime, "onnxruntime.dll"));
        void** apiBase = ((delegate* unmanaged<void**>)NativeLibrary.GetExport(library, "OrtGetApiBase"))();
        void** api = ((delegate* unmanaged<uint, void**>)apiBase[0])(OrtApiVersion);
        if (api is null) return;
        s_api = api;
        nint env;
        fixed (byte* id = "thoughts\0"u8) Check(((delegate* unmanaged<int, byte*, nint*, nint>)api[CreateEnv])(3, id, &env));
        s_env = env;

        string qnn = Path.Combine(runtime, "onnxruntime_providers_qnn.dll");
        if (!File.Exists(qnn)) return;
        try
        {
            fixed (byte* name = "QNNExecutionProvider\0"u8)
            fixed (char* path = qnn)
                Check(((delegate* unmanaged<nint, byte*, char*, nint>)api[RegisterExecutionProviderLibrary])(env, name, path));
            nint* devices;
            nuint count;
            Check(((delegate* unmanaged<nint, nint**, nuint*, nint>)api[GetEpDevices])(env, &devices, &count));
            for (nuint i = 0; i < count; i++)
            {
                string? provider = Marshal.PtrToStringUTF8((nint)((delegate* unmanaged<nint, byte*>)api[EpDeviceName])(devices[i]));
                int hardware = ((delegate* unmanaged<nint, int>)api[HardwareDeviceType])(((delegate* unmanaged<nint, nint>)api[EpDeviceHardware])(devices[i]));
                if (provider == "QNNExecutionProvider" && hardware == HardwareNpu) s_npu = devices[i];
            }
        }
        catch (InvalidOperationException)
        {
            s_npu = 0; // No usable NPU here. The CPU it is.
        }
    }

    static nint OpenOnCpu(string dir)
    {
        nint options;
        Check(((delegate* unmanaged<nint*, nint>)s_api[CreateSessionOptions])(&options));
        try
        {
            return Open(Path.Combine(dir, "e5-small", "model.onnx"), options);
        }
        finally
        {
            ((delegate* unmanaged<nint, void>)s_api[ReleaseSessionOptions])(options);
        }
    }

    // The first time compiles the model for this NPU and keeps the result, which takes it from seconds to a third of one.
    static nint OpenOnNpu(string dir)
    {
        string compiled = Path.Combine(dir, "e5-small", $"e5-{NpuLength}.ctx.onnx");
        bool haveCompiled = File.Exists(compiled);
        nint options;
        Check(((delegate* unmanaged<nint*, nint>)s_api[CreateSessionOptions])(&options));
        try
        {
            Dimension(options, "batch_size\0"u8, 1);
            Dimension(options, "sequence_length\0"u8, NpuLength);
            Config(options, "session.disable_cpu_ep_fallback\0"u8, "1\0"u8);
            if (!haveCompiled)
            {
                Config(options, "ep.context_enable\0"u8, "1\0"u8);
                Config(options, "ep.context_file_path\0"u8, Encoding.UTF8.GetBytes(compiled + "\0"));
            }
            nint device = s_npu;
            Check(((delegate* unmanaged<nint, nint, nint*, nuint, byte**, byte**, nuint, nint>)s_api[AppendExecutionProviderV2])(options, s_env, &device, 1, null, null, 0));
            return Open(haveCompiled ? compiled : Path.Combine(dir, "e5-small", "model.onnx"), options);
        }
        finally
        {
            ((delegate* unmanaged<nint, void>)s_api[ReleaseSessionOptions])(options);
        }
    }

    static nint Open(string model, nint options)
    {
        nint session;
        fixed (char* path = model) Check(((delegate* unmanaged<nint, char*, nint, nint*, nint>)s_api[CreateSession])(s_env, path, options, &session));
        return session;
    }

    static void Dimension(nint options, ReadOnlySpan<byte> name, long value)
    {
        fixed (byte* n = name) Check(((delegate* unmanaged<nint, byte*, long, nint>)s_api[AddFreeDimensionOverride])(options, n, value));
    }

    static void Config(nint options, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        fixed (byte* k = key)
        fixed (byte* v = value)
            Check(((delegate* unmanaged<nint, byte*, byte*, nint>)s_api[AddConfigEntry])(options, k, v));
    }

    static void Check(nint status)
    {
        if (status == 0) return;
        string message = Marshal.PtrToStringUTF8((nint)((delegate* unmanaged<nint, byte*>)s_api[ErrMsg])(status)) ?? "ONNX Runtime failed.";
        ((delegate* unmanaged<nint, void>)s_api[ReleaseStatus])(status);
        throw new InvalidOperationException(message);
    }

    /// <summary>A unit vector. Text needs its <c>query: </c> or <c>passage: </c> prefix, which is how this model tells them apart.</summary>
    public float[] Embed(string text)
    {
        var pieces = _tokenizer.EncodeToIds(text);
        int used = Math.Min(pieces.Count + 2, NpuLength);
        int length = _onNpu ? NpuLength : used;
        var ids = new long[length];
        var mask = new long[length];
        var types = new long[length];
        Array.Fill(ids, 1L); // <pad>

        // This model's ids are SentencePiece's plus 1, with <s> as 0 and </s> as 2.
        ids[0] = 0;
        mask[0] = 1;
        int n = 1;
        foreach (int piece in pieces)
        {
            if (n >= used - 1) break;
            ids[n] = piece + 1;
            mask[n] = 1;
            n++;
        }
        ids[n] = 2;
        mask[n] = 1;
        n++;

        var api = s_api;
        long* shape = stackalloc long[2] { 1, length };
        nuint bytes = (nuint)(length * sizeof(long));
        var names = new[] { "input_ids\0"u8.ToArray(), "attention_mask\0"u8.ToArray(), "token_type_ids\0"u8.ToArray() };
        var output = "last_hidden_state\0"u8.ToArray();
        nint v0 = 0, v1 = 0, v2 = 0, result = 0;
        fixed (long* pi = ids, pm = mask, pt = types)
        fixed (byte* n0 = names[0], n1 = names[1], n2 = names[2], on = output)
        {
            try
            {
                var tensor = (delegate* unmanaged<nint, void*, nuint, long*, nuint, int, nint*, nint>)api[CreateTensor];
                Check(tensor(_memory, pi, bytes, shape, 2, TensorInt64, &v0));
                Check(tensor(_memory, pm, bytes, shape, 2, TensorInt64, &v1));
                Check(tensor(_memory, pt, bytes, shape, 2, TensorInt64, &v2));
                byte** inputNames = stackalloc byte*[3] { n0, n1, n2 };
                nint* inputs = stackalloc nint[3] { v0, v1, v2 };
                byte* outputName = on;
                Check(((delegate* unmanaged<nint, nint, byte**, nint*, nuint, byte**, nuint, nint*, nint>)api[RunSession])(_session, 0, inputNames, inputs, 3, &outputName, 1, &result));
                float* data;
                Check(((delegate* unmanaged<nint, void**, nint>)api[GetTensorData])(result, (void**)&data));

                // The mean over the real tokens, normalized.
                var vector = new float[Dimensions];
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < Dimensions; j++)
                        vector[j] += data[i * Dimensions + j];
                double sum = 0;
                foreach (float f in vector) sum += f * f;
                float norm = (float)Math.Sqrt(sum);
                if (norm > 0f)
                    for (int j = 0; j < Dimensions; j++)
                        vector[j] /= norm;
                return vector;
            }
            finally
            {
                var release = (delegate* unmanaged<nint, void>)api[ReleaseValue];
                if (result != 0) release(result);
                if (v0 != 0) release(v0);
                if (v1 != 0) release(v1);
                if (v2 != 0) release(v2);
            }
        }
    }

    public void Dispose()
    {
        ((delegate* unmanaged<nint, void>)s_api[ReleaseSession])(_session);
        ((delegate* unmanaged<nint, void>)s_api[ReleaseMemoryInfo])(_memory);
    }
}
