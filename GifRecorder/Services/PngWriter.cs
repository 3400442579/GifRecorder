﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace GifRecorder.Services
{
    public class PngWriter : IDisposable
    {
        #region Fields
        readonly BinaryWriter _writer;
        Stream OutStream;
        #endregion

        #region Props
        public int DefaultFrameDelay { get; set; }
        public int Repeat { get; }

        /// <summary>
        /// The current count of Frames added to the Animation.
        /// </summary>
        public long FrameCount { get; private set; }

        private long FrameCountPosition { get; set; }
        private int ChunkSequenceNumber { get; set; }
        private int x { get; }
        private int y { get; }
        #endregion

        /// <summary>
        /// Creates a new instance of PngWriter.
        /// </summary>
        /// <param name="OutStream">The <see cref="Stream"/> to output the Gif to.</param>
        /// <param name="DefaultFrameDelay">Default Delay between consecutive frames... FrameRate = 1000 / DefaultFrameDelay.</param>
        /// <param name="Repeat">No of times the Png should repeat... 0 to repeat indefinitely.</param>
        public PngWriter(Stream OutStream, int x, int y, int DefaultFrameDelay = 500, int Repeat = 0)
        {
            if (OutStream == null)
                throw new ArgumentNullException(nameof(OutStream));

            if (DefaultFrameDelay <= 0)
                throw new ArgumentOutOfRangeException(nameof(DefaultFrameDelay));

            if (Repeat < 0)
                throw new ArgumentOutOfRangeException(nameof(Repeat));

            _writer = new BinaryWriter(OutStream);
            this.DefaultFrameDelay = DefaultFrameDelay;
            this.Repeat = Repeat;
            FrameCount = 0;
            ChunkSequenceNumber = 0;
            this.x = x;
            this.y = y;
            this.OutStream = OutStream;
        }

        #region Private methods

        private void write_Signature()
        {
            Byte[] signature =
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
            };
            write(signature);
        }

        private void write_IHDR(Stream png) // Image Header
        {
            var ihdr = find_IHDR(png);
            if (ihdr != null)
            {
                write(ihdr[0]);
            }
        }

        private void write_acTL_placeholder() // Animation Control Chunk
        {
            FrameCountPosition = _writer.BaseStream.Position;
            _writer.Write(getSwappedArray(8));
            _writer.Write("acTL".ToCharArray());
            _writer.Write(0); // Number of frames
            _writer.Write(getSwappedArray(Repeat)); // Number of times to loop this APNG.  0 indicates infinite looping.
            _writer.Write(0);
        }

        private void write_tEXt_signature()
        {
            //Prepare data
            var text = "tEXt".ToCharArray().Select(c => (byte)c).ToArray();
            var text2 = "Software CMK " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + "_" +
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var length = text2.Length;
            var lengthArray = getSwappedArray(length);

            //Assemble data
            byte[] text3 = new byte[text.Length + text2.Length];
            text.CopyTo(text3, 0);
            text2.ToCharArray().Select(c => (byte)c).ToArray().CopyTo(text3, 4);

            //Write data
            _writer.Write(lengthArray);
            _writer.Write(text3);
            _writer.Write(getSwappedCrc(text3));
        }

        private void write_fcTL(int x, int y, int offsetX, int offsetY, short frameDelay) // Frame Control Chunk
        {
            //Prepare data
            List<Byte> chunk = new List<byte>();
            Byte[] _ChunkSequenceNumber = getSwappedArray(ChunkSequenceNumber);
            Byte[] _x = getSwappedArray(x);
            Byte[] _y = getSwappedArray(y);
            Byte[] _offsetX = getSwappedArray(offsetX);
            Byte[] _offsetY = getSwappedArray(offsetY);
            Byte[] _DefaultFrameDelay = getSwappedArray(frameDelay);
            Byte[] _FrameCount2 = getSwappedArray((short)FrameCount);

            //Assemble data
            chunk.AddRange(new Byte[] { 0, 0, 0, 26 });
            chunk.AddRange("fcTL".ToCharArray().Select(c => (byte)c).ToArray());
            chunk.AddRange(_ChunkSequenceNumber);
            chunk.AddRange(_x);
            chunk.AddRange(_y);
            chunk.AddRange(_offsetX);
            chunk.AddRange(_offsetY);
            chunk.AddRange(_DefaultFrameDelay);
            chunk.AddRange(new Byte[] { 3, 232 });
            chunk.AddRange(new Byte[] { 0, 1 });

            //Write data
            _writer.Write(chunk.ToArray());
            _writer.Write(getSwappedCrc(chunk.Skip(4).ToArray()));

            //Increase counters
            FrameCount++;
            ChunkSequenceNumber++;
        }

        private void write_IEND()
        {
            Byte[] iend =
            {
                0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
            };
            write(iend);
        }

        private void write_IDAT(Stream png)
        {
            List<Byte[]> idatList = find_IDAT(png);
            foreach (var idat in idatList)
            {
                if (idat != null)
                {
                    write(idat);
                }
            }
        }

        private void write_fdAT(Stream png)
        {
            List<Byte[]> idatList = find_IDAT(png);
            foreach (var idat in idatList)
            {
                if (idat != null)
                {
                    //Prepare data
                    Byte[] _ChunkSequenceNumber = getSwappedArray(ChunkSequenceNumber);
                    var length = idat.Count() - 8;
                    var lengthArray = getSwappedArray(length);
                    Byte[] fdAT = new byte[idat.Count()];
                    var sign = "fdAT".ToCharArray().Select(c => (byte)c).ToArray();
                    var idat2 = idat.Take(idat.Count() - 4).ToArray();
                    idat2 = idat2.Skip(8).ToArray();
                    var data = fdAT.Skip(4).ToArray();

                    //Assemble data
                    lengthArray.CopyTo(fdAT, 0);
                    sign.CopyTo(fdAT, 4);
                    _ChunkSequenceNumber.CopyTo(fdAT, 8);
                    idat2.CopyTo(fdAT, 12);

                    //Write data
                    _writer.Write(fdAT);
                    _writer.Write(getSwappedCrc(data));

                    //Increase counter
                    ChunkSequenceNumber++;
                }
            }
        }

        private byte[] getSwappedCrc(byte[] chunk)
        {
            var crc32 = new CrcCalculator();
            var crc = crc32.GetCRC32(chunk);
            var crcArray = BitConverter.GetBytes(crc);
            Array.Reverse(crcArray);
            return crcArray;
        }

        private byte[] getSwappedArray(int i)
        {
            Byte[] bytes = BitConverter.GetBytes(i);
            Array.Reverse(bytes);
            return bytes;
        }

        private byte[] getSwappedArray(short s)
        {
            Byte[] bytes = BitConverter.GetBytes(s);
            Array.Reverse(bytes);
            return bytes;
        }

        private List<Byte[]> find_IHDR(Stream png)
        {
            return find(png, "IHDR".ToCharArray());
        }

        private List<Byte[]> find_IDAT(Stream png)
        {
            return find(png, "IDAT".ToCharArray());
        }

        private List<Byte[]> find(Stream png, Char[] search)
        {
            List<Byte[]> result = new List<byte[]>();
            var searchBytes = search.Select(c => (byte)c).ToArray();
            Byte[] bytes = new Byte[search.Length];
            int i = 0;
            int found = 0;
            while (i < png.Length - 4)
            {
                png.Flush();
                png.Position = i;
                var debug = png.Read(bytes, 0, search.Length);
                i++;
                if (bytes.SequenceEqual(searchBytes))
                {
                    Byte[] rawLength = new Byte[4];
                    png.Position -= 8;
                    png.Read(rawLength, 0, 4);
                    Array.Reverse(rawLength);
                    UInt32 length = BitConverter.ToUInt32(rawLength, 0);
                    result.Add(new Byte[length + 12]);
                    png.Position -= 4;
                    png.Read(result[found], 0, (int)(length + 12));
                    found++;
                }
            }
            return result;
        }

        private void write(Byte[] data)
        {
            _writer.Write(data);
        }

        private void write_acTL()
        {
            //Prepare data
            Byte[] _FrameCount = getSwappedArray((int)FrameCount);
            Byte[] _Repeat = getSwappedArray(Repeat);

            //Assemble data
            List<byte> chunk = new List<byte>()
            {
                0,0,0,8
            };
            chunk.AddRange("acTL".ToCharArray().Select(c => (byte)c).ToArray());
            chunk.AddRange(_FrameCount);
            chunk.AddRange(_Repeat);

            //Write data
            _writer.Seek((int)FrameCountPosition, SeekOrigin.Begin);
            _writer.Write(chunk.ToArray());
            _writer.Write(getSwappedCrc(chunk.Skip(4).ToArray()));

            //Increase counter
            FrameCount++;
        }

        #endregion

        /// <summary>
        /// Adds a frame to this animation.
        /// </summary>
        /// <param name="Image">The image to add</param>
        /// <param name="offsetX">X offset to render the image</param>
        /// <param name="offsetY">Y offset to render the image</param>
        public void WriteFrame(Image image, short frameDelay, int offsetX = 0, int offsetY = 0)
        {
            using (Stream png = new MemoryStream())
            {
                image.Save(png, ImageFormat.Png);
                if (FrameCount == 0)
                {
                    write_Signature();
                    write_IHDR(png);
                    write_tEXt_signature();
                    write_acTL_placeholder();
                }
                write_fcTL(image.Width, image.Height, offsetX, offsetY, frameDelay);
                if (FrameCount == 1)
                    write_IDAT(png);
                else
                    write_fdAT(png);
            }
        }

        /// <summary>
        /// Frees all resources used by this object.
        /// </summary>
        public void Dispose()
        {
            write_IEND();
            write_acTL();
        }
    }
}
