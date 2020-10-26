﻿using System;
using System.Diagnostics;
using System.IO.Ports;

namespace BitFab.KW1281Test
{
    interface IInterface : IDisposable
    {
        /// <summary>
        /// Read a byte from the interface.
        /// </summary>
        /// <returns>The byte.</returns>
        byte ReadByte();

        /// <summary>
        /// Write a byte to the interface and receive its echo.
        /// </summary>
        /// <param name="b">The byte to write.</param>
        void WriteByte(byte b);

        /// <summary>
        /// Send a byte at 5 baud manually to the port.
        /// https://www.blafusel.de/obd/obd2_kw1281.html
        /// </summary>
        /// <param name="b">The byte to write.</param>
        void BitBang5Baud(byte b);
    }

    class Interface : IInterface
    {
        public Interface(string portName, int baudRate)
        {
            _port = new SerialPort(portName)
            {
                BaudRate = baudRate,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                RtsEnable = false,
                DtrEnable = true,
                ReadTimeout = (int)TimeSpan.FromSeconds(5).TotalMilliseconds,
                WriteTimeout = (int)TimeSpan.FromSeconds(5).TotalMilliseconds
            };

            _port.Open();
        }

        public void Dispose()
        {
            _port.Close();
        }

        /// <summary>
        /// Read a byte from the interface.
        /// </summary>
        /// <returns>The byte.</returns>
        public byte ReadByte()
        {
            var b = (byte)_port.ReadByte();
            return b;
        }

        /// <summary>
        /// Write a byte to the interface and receive its echo.
        /// </summary>
        /// <param name="b">The byte to write.</param>
        public void WriteByte(byte b)
        {
            _buf[0] = b;
            _port.Write(_buf, 0, 1);
            var echo = _port.ReadByte();
            if (echo != b)
            {
                throw new InvalidOperationException($"Wrote 0x{b:X2} to port but echo was 0x{echo:X2}");
            }
        }

        /// <summary>
        /// Send a byte at 5 baud manually to the port.
        /// https://www.blafusel.de/obd/obd2_kw1281.html
        /// </summary>
        /// <param name="b">The byte to write.</param>
        public void BitBang5Baud(byte b)
        {
#if !NET40
            // Disable garbage collection during this time-critical 
            bool noGc = GC.TryStartNoGCRegion(1024 * 1024);
#endif

            const int bitsPerSec = 5;
            const int msecPerSec = 1000;
            const int msecPerBit = msecPerSec / bitsPerSec;

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            // Delay the appropriate amount and then set/clear the TxD line
            Action<bool> BitBang = bit =>
            {
                while ((msecPerBit - stopWatch.ElapsedMilliseconds) > 0)
                    ;
                // Thread.Sleep((int)(msecPerBit - stopWatch.ElapsedMilliseconds));
                stopWatch.Restart();
                _port.BreakState = !bit;
            };

            BitBang(false); // Start bit

            bool parity = true; // XORed with each bit to produce odd parity
            for (int i = 0; i < 7; i++)
            {
                bool bit = (b & 1) == 1;
                parity ^= bit;
                b >>= 1;

                BitBang(bit);
            }

            BitBang(parity);

            BitBang(true); // Stop bit

#if !NET40
            if (noGc)
            {
                GC.EndNoGCRegion();
            }
#endif

            // Throw away anything that might be in the receive buffer
            _port.DiscardInBuffer();
        }

        private SerialPort _port;

        private byte[] _buf = new byte[1];
    }
}
