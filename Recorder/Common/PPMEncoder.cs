using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;

namespace DiscoPanda
{
    public class PPMEncoder
    {
        NativeArray<byte> ppmBytes;
        NativeArray<byte> ppmHeader;
        JobHandle imageEncoderJobHandle;

        public int Size { get; private set; }

        public PPMEncoder(int width, int height)
        {
            Size = 18 + (width * height) * 3;

            var headerString = $"P6\n{width} {height}\n255\n";
            var headerBytes = System.Text.Encoding.ASCII.GetBytes(headerString);

            ppmBytes = new NativeArray<byte>(Size, Allocator.Persistent);
            ppmHeader = new NativeArray<byte>(headerBytes, Allocator.Persistent);
        }

        public void CompleteJobs()
        {
            imageEncoderJobHandle.Complete();
        }

        public void Dispose()
        {
            CompleteJobs();

            if (ppmBytes.IsCreated)
                ppmBytes.Dispose();

            if (ppmHeader.IsCreated)
                ppmHeader.Dispose();
        }

        public void CopyTo(byte[] bytes)
        {
            ppmBytes.CopyTo(bytes);
        }

        public void Encode(NativeArray<byte> bytes)
        {
            var pixelCount = bytes.Length / 4;
            var headerLength = ppmHeader.Length;

            var headerJob = new CopyArrayJob()
            {
                input = ppmHeader,
                output = ppmBytes
            }.Schedule();

            imageEncoderJobHandle = new PPMEncoderJob()
            {
                headerLength = headerLength,
                input = bytes,
                output = ppmBytes
            }.Schedule(pixelCount, 64, headerJob);
        }

        [BurstCompile]
        struct CopyArrayJob : IJob
        {
            [ReadOnly] public NativeArray<byte> input;
            [WriteOnly] public NativeArray<byte> output;

            public void Execute()
            {
                for (int i = 0; i < input.Length; i++)
                {
                    output[i] = input[i];
                }
            }
        }

        [BurstCompile]
        struct PPMEncoderJob : IJobParallelFor
        {
            [ReadOnly] public int headerLength;
            [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<byte> input;
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> output;

            public void Execute(int i)
            {
                int inputIndex = (i * 4);
                int outputIndex = (i * 3) + headerLength;

                output[outputIndex] = input[inputIndex];
                output[outputIndex + 1] = input[inputIndex + 1];
                output[outputIndex + 2] = input[inputIndex + 2];
            }
        }
    }
}