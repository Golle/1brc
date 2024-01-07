
/*
 * This is a .NET implementation of the 1brc challenge https://github.com/gunnarmorling/1brc
 * The measurement file is in UTF8 and has LF line endings. This is what the original challenge has and what we'll use in this code as well.
 * CRLF and UTF16 will not be used. 
 */


using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Challenge;
using Microsoft.Win32.SafeHandles;

#if DEBUG
var processorCount = 1;
const string DataPath = @"O:\git\1brc\data";
const string MeasurementsFilePath = @$"{DataPath}\measurements_small.txt";
//const string MeasurementsFilePath = @$"{DataPath}\measurements.txt";
#else
//NOTE(Jens): This wont work if the executable is not run from the root. But we don't want any allocations or weird things so we'll keep it like this for now :)
const string DataPath = @"data\";
//const string MeasurementsFilePath = @$"{DataPath}\measurements_small.txt";
//const string MeasurementsFilePath = @$"{DataPath}\measurements_medium.txt";
const string MeasurementsFilePath = @$"{DataPath}\measurements.txt";
var processorCount = Environment.ProcessorCount - 1;

#endif

unsafe
{
    var timer = Stopwatch.StartNew();
    using var handle = File.OpenHandle(MeasurementsFilePath, FileMode.Open, FileAccess.Read, FileShare.None, FileOptions.Asynchronous | FileOptions.RandomAccess);
    var contexts = new DataContext[processorCount];
    SplitIntoChunks(handle, contexts);
    const int BumpAllocatorSize = 1 * 1024 * 1024;
    var bumpAllocator = new BumpAllocator((byte*)NativeMemory.Alloc(BumpAllocatorSize), BumpAllocatorSize);
    {
        using var pool = WorkerPool.CreateAndExcute(contexts, &bumpAllocator, &ProcessChunk);
    }
    timer.Stop();
    var baseMeasurements = contexts[0].Measurements!;
    for (var i = 1; i < contexts.Length; ++i)
    {
        foreach (var measurement in contexts[i].Measurements!)
        {

            ref var theData = ref CollectionsMarshal.GetValueRefOrAddDefault(baseMeasurements, measurement.Key, out var exist);
            if (exist)
            {
                var things = measurement.Value;
                theData.Max = MathHelper.Max(theData.Max, things.Max);
                theData.Min = MathHelper.Min(theData.Min, things.Min);
                theData.Count += things.Count;
                theData.Sum += things.Sum;
            }
            else
            {
                theData = measurement.Value;
            }
        }
    }

    var builder = new StringBuilder(10_000);
    builder.AppendLine("{");
    foreach (var value in baseMeasurements.Values)
    {
        builder.AppendLine($"\t{value.Name}={value.Min / 10.0f:0.0}/{value.CalculateMean() / 10.0f:0.0}/{value.Max / 10.0f:0.0}");
    }

    builder.AppendLine("}");
    timer.Stop();
    Console.WriteLine(builder.ToString());
    Console.WriteLine($"Finished processing in {timer.Elapsed}");
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static unsafe ulong FakeHash(Span<byte> buffer)
{
    if (buffer.Length < sizeof(ulong))
    {
        // probably slooow
        ulong value = 0;
        foreach (var b in buffer)
        {
            value += b;
        }
        return value;
    }

    fixed (byte* ptr = buffer)
    {
        return *(ulong*)ptr;
    }
}

static unsafe void ProcessChunk(int dataIndex, Span<DataContext> contexts, BumpAllocator* allocator)
{
    const int DefaultDataCapacity = 500;// Tweak this
    ref var context = ref contexts[dataIndex];
    var definition = context.Chunk;
    var handle = context.Handle;
    var measurements = context.Measurements = new Dictionary<ulong, Data>(DefaultDataCapacity);
    DebugWriteLine($"\t Thread: {Thread.CurrentThread.ManagedThreadId} Offset: {definition.Offset} Length: {definition.Length}");

    const uint MaxReadSize = 4 * 1024 * 1024; // 4Mb per read (tweak this)
    var buffer = NativeMemory.Alloc(MaxReadSize); // yeah, we don't really care if we leak memory.
    var bufferSpan = new Span<byte>(buffer, (int)MaxReadSize);

    var offset = definition.Offset;
    var bytesLeft = definition.Length;
    int bytesRead;
    do
    {
        var minRead = MathHelper.Min((int)MaxReadSize, (int)bytesLeft);
        var dataRead = bufferSpan[..minRead];
        bytesRead = RandomAccess.Read(handle, dataRead, offset);
        bytesLeft -= bytesRead;

        // test different values
        const uint ThreshholdForNewRead = 128;

        var data = dataRead;
        var remainingBytes = bytesRead;
        while (remainingBytes > ThreshholdForNewRead)
        {
            //NOTE(Jens): Do this with SIMD later
            var index = data.IndexOf((byte)';');
            var name = data[..index];
            var nameHash = FakeHash(name); // might need some adjustments :) 
            var valueSpan = data[(index + 1)..];
            var value = ParseValue(valueSpan, out var count);

            ref var measurement = ref CollectionsMarshal.GetValueRefOrAddDefault(measurements, nameHash, out var exist);
            if (!exist)
            {
                measurement.Name = allocator->Alloc(name);
                measurement.Min = value;
                measurement.Max = value;
                measurement.Sum = value;
                measurement.Count = 1;
            }
            else
            {
                measurement.Max = MathHelper.Max(measurement.Max, value);
                measurement.Min = MathHelper.Min(measurement.Min, value);
                measurement.Sum += value;
                measurement.Count++;
            }

            var sizeConsumed = index + count + 1;
            data = data[sizeConsumed..];
            remainingBytes -= sizeConsumed;
        }

        offset += (bytesRead - remainingBytes);

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        static int ParseValue(Span<byte> buffer, out int count)
        {
            var multiplier = 1;
            var value = 0;
            var i = 0;

            while (true)
            {
                var character = buffer[i++];
                switch (character)
                {
                    case (byte)'-':
                        multiplier = -1;
                        break;
                    case (byte)'.':
                        break;
                    case (byte)'\n':
                        count = i;
                        return value * multiplier;
                    default:
                        var number = character - '0';
                        value = value * 10 + number;
                        break;
                }
            }
        }

    } while (bytesRead > 0 && bytesLeft > 0);

    DebugWriteLine($"Finished reading file. Measurements = {measurements.Count}");
}

static void SplitIntoChunks(SafeFileHandle handle, Span<DataContext> contexts)
{
    Span<byte> buffer = stackalloc byte[128];
    var definitionIndex = 0;
    var fileSize = RandomAccess.GetLength(handle);
    var minChunkSize = fileSize / contexts.Length;
    var offset = 0L;

    for (var i = 0; i < contexts.Length; ++i)
    {
        var bytesRead = RandomAccess.Read(handle, buffer, offset + minChunkSize);
        if (bytesRead == 0)
        {
            contexts[definitionIndex] = new DataContext
            {
                Chunk = { Length = fileSize - offset, Offset = offset },
                Handle = handle
            };
            return;
        }

        // this can probably be improvd, but its only executed X number of times, where X = number of cores.
        var index = buffer[..bytesRead].IndexOf((byte)'\n');
        Debug.Assert(index != -1);
        var length = minChunkSize + index;
        contexts[definitionIndex++] = new DataContext
        {
            Chunk = { Length = length, Offset = offset },
            Handle = handle
        };
        offset += length + 1;
    }
}

[Conditional("DEBUG")]
static void DebugWriteLine(string message)
    => Console.WriteLine(message);


public struct DataContext
{
    public SafeFileHandle Handle;
    public ChunkDefinition Chunk;
    public Dictionary<ulong, Data>? Measurements;
}

public struct ChunkDefinition
{
    public long Offset;
    public long Length;
}

public struct Data
{
    public Utf8Span Name;
    public int Min;
    public int Max;
    public int Sum;
    public int Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float CalculateMean() => (Sum / (float)Count);
}
