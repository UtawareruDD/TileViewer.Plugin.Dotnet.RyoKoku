using Microsoft.VisualBasic.FileIO;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace TileViewer.ManagedPlugin.Decoders
{
    /// <summary>
    ///  dll/so ==> TileDecoderNativeExports(unsafe) ==> ManagedTileViewerPlugin ==> MainTileDecoder
    /// Demonstration of a configurable tile decoder.
    /// Decoder that treats each byte as four packed 2-bit values mapped to individual logical pages.
    /// PS2 SLPM-66535 Ryu-Koku font.bin decode
    /// </summary>
    public sealed class MainTileDecoder : IConfigurableTileDecoder
    {
        private bool _opened;

        //MainTileDecoder
        private const int PageCount = 4;
        private static readonly Pixel[] Cp4 = new Pixel[] {
            new Pixel(0x00,0x00,0x00,0xFF) ,// 0: Black
            new Pixel(0xFF,0xFF,0xFF,0xFF),// 1: White
            new Pixel(0x80,0x80,0x80,0xFF), // 3: Gray
            new Pixel(0x40,0x40,0x40,0xFF), // 2: DarkGray
        };
        private int _selectedPage;

        //isdecodeall
        //true use decodeall
        //false use decodeone
        public bool SupportsDecodeAll => true;
        public string DefaultPluginName => "Decoder TileViewer.Plugin.Dotnet.RyoKoku PS2";
        public string DisplayVersion => "v1.0.0";
        public string Description => "Expands packed 2-bit pages using a managed implementation.";
        public uint RequiredTileViewerVersion => 400;

        public PluginStatus Open(TileDecodeOpenRequest request)
        {
            _opened = true;
            return PluginStatus.Ok;
        }

        public PluginStatus Close(TileDecodeCloseRequest request)
        {
            _opened = false;
            return PluginStatus.Ok;
        }

        public PluginStatus DecodeOne(TileDecodeOneRequest request, out Pixel pixel)
        {
            if (!_opened)
            {
                pixel = default;
                return PluginStatus.OpenError;
            }

            if (request.Data.IsEmpty)
            {
                pixel = default;
                return PluginStatus.FormatError;
            }

            if (!TryGetPixelByte(request.Data.Span, request.Format, request.Position, out byte packedValue))
            {
                pixel = default;
                return PluginStatus.RangeError;
            }

            pixel = DecodePackedValue(packedValue, _selectedPage);
            return PluginStatus.Ok;
        }

        public static IEnumerable<T> MergeRoundRobinBlocksUnlimited<T>(
            IReadOnlyList<Queue<T>> queues, int blockLen = 256)
        {
            if (queues == null || queues.Count == 0) yield break;

            while (true)
            {
                bool anyYieldedInCycle = false;

                for (int qi = 0; qi < queues.Count; qi++)
                {
                    var q = queues[qi];
                    int taken = 0;

                    while (taken < blockLen && q.Count > 0)
                    {
                        yield return q.Dequeue();
                        taken++;
                        anyYieldedInCycle = true;
                    }
                }

                if (!anyYieldedInCycle) yield break; 
            }
        }

        public PluginStatus DecodeAll(TileDecodeAllRequest request)
        {
            if (!_opened)
            {
                return PluginStatus.OpenError;
            }

            ReadOnlySpan<byte> source = request.Data.Span;
            Span<Pixel> destination = request.Destination.Memory.Span;

            if (destination.Length < source.Length)
            {
                return PluginStatus.RangeError;
            }

            Debug.WriteLine(_selectedPage);
            if (_selectedPage == -1)
            {
                if (destination.Length < source.Length * 4)
                {
                    Debug.WriteLine("RangeError Need Select 2bpp" );
                    return PluginStatus.RangeError;
                }
                Queue<Pixel> queue0 = new Queue<Pixel>();
                Queue<Pixel> queue1 = new Queue<Pixel>();
                Queue<Pixel> queue2 = new Queue<Pixel>();
                Queue<Pixel> queue3 = new Queue<Pixel>();
                for (int i = 0; i < source.Length; i++)
                {
                    byte packedValue = source[i];
                    queue0.Enqueue(DecodePackedValue(packedValue, 0));
                    queue1.Enqueue(DecodePackedValue(packedValue, 1));
                    queue2.Enqueue(DecodePackedValue(packedValue, 2));
                    queue3.Enqueue(DecodePackedValue(packedValue, 3));
                }
                MergeRoundRobinBlocksUnlimited(new List<Queue<Pixel>>() {  queue3, queue2, queue1, queue0, }, 256).ToArray().CopyTo(destination);
            }
            else
            {
                for (int i = 0; i < source.Length; i++)
                {
                    destination[i] = DecodePackedValue(source[i], _selectedPage);
                }
            }

            return PluginStatus.Ok;
        }

        public PluginStatus Preprocess(TileDecodePipelineRequest request)
        {
            return PluginStatus.Ok;
        }

        public PluginStatus Postprocess(TileDecodePipelineRequest request)
        {
            return PluginStatus.Ok;
        }

        public string BuildConfigurationJson(string pluginName)
        {
            PluginConfiguration configuration = new()
            {
                Name = string.IsNullOrWhiteSpace(pluginName) ? DefaultPluginName : pluginName,
                Description = Description,
                Version = DisplayVersion,
                RequiredTileViewerVersion = RequiredTileViewerVersion,
                Options = BuildOptionSnapshot(),
            };

            return JsonSerializer.Serialize(configuration, PluginConfigurationJsonContext.Default.PluginConfiguration);
        }

        public void ApplyOptions(IReadOnlyList<PluginOption> options, Action<string> log)
        {
            if (options is null)
            {
                return;
            }

            foreach (PluginOption option in options)
            {
                if (string.IsNullOrWhiteSpace(option.Name))
                {
                    continue;
                }

                if (string.Equals(option.Name, "page", StringComparison.OrdinalIgnoreCase))
                {
                    if (option.Value is not null)
                    {
                        Debug.WriteLine(option.Value.ToString());
                        if (option.Value.ToString() == "all" || option.Value.ToString() == "4")
                            _selectedPage = -1;
                        else
                            _selectedPage = int.Parse(option.Value.ToString() ?? "0");
                        log?.Invoke($"Active decode page switched to '{option.Value.ToString()}'.");
                    }
                }
                //if (string.Equals(option.Name, "TestName", StringComparison.OrdinalIgnoreCase))
                //{
                //    if (option.Value is not null)
                //    {
                //        if (option.Value.ToString() == "test1")
                //            log?.Invoke($"Test option set to '{option.Value.ToString()}'.");
                //        else if (option.Value.ToString() == "test2")
                //            log?.Invoke($"Test option set to '{option.Value.ToString()}'.");
                //        else if (option.Value.ToString() == "test3")
                //            log?.Invoke($"Test option set to '{option.Value.ToString()}'.");
                //    }
                //}
            }
        }

        private Pixel DecodePackedValue(byte packedValue,int _selectedPage_c)
        {
            Pixel p = Cp4[0];
            if (_selectedPage_c != -1)
            {
                int shift = GetShiftForPage(_selectedPage_c);
                int index = (packedValue >> shift) & 0x03;
                p = Cp4[index];
            }
            return p;
        }

        private List<PluginOption> BuildOptionSnapshot()
        {
            return new List<PluginOption>
            {
                //new PluginOption
                //{
                //    Name = "RemainIndex",
                //    Type = "bool",
                //    Help = "Preserve the original tile ordering when decoding.",
                //    Value = "true",
                //},
                new PluginOption
                {
                    Name = "Page",
                    Type = "enum",
                    Choices = new List<string>(){ "0","1","2","3","all"},
                    Help = "Select which 2-bit page should be expanded when decoding.",
                    Value = 4,
                },
                // new PluginOption
                //{
                //    Name = "TestName",
                //    Type = "enum",
                //    Choices = new List<string>(){ "test1","test2","test3"},
                //    Help = "test option",
                //    Value = "test1",
                //}
            };
        }

        private static bool TryGetPixelByte(ReadOnlySpan<byte> data, TileFormat format, TilePosition position, out byte value)
        {
            try
            {
                int bytesPerPixel = Math.Max(1, format.BitsPerPixel / 8);
                int tileIndexOffset = checked(position.TileIndex * (int)format.BytesPerTile);
                int pixelOffset = checked((position.Y * (int)format.Width + position.X) * bytesPerPixel);
                int offset = checked(tileIndexOffset + pixelOffset);

                if ((uint)offset >= (uint)data.Length)
                {
                    value = 0;
                    return false;
                }

                value = data[offset];
                return true;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetShiftForPage(int page)
        {
            return (PageCount - page - 1) * 2;
        }

    }
}
