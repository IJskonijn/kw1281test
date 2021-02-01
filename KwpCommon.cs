﻿using BitFab.KW1281Test.Interface;
using System;
using System.Diagnostics;

namespace BitFab.KW1281Test
{
    interface IKwpCommon
    {
        int WakeUp(byte controllerAddress, bool evenParity = false);

        byte ReadByte();

        /// <summary>
        /// Write a byte to the interface and receive its echo.
        /// </summary>
        /// <param name="b">The byte to write.</param>
        void WriteByte(byte b);

        byte ReadAndAckByte();

        void ReadComplement(byte b);
    }

    class KwpCommon : IKwpCommon
    {
        public int WakeUp(byte controllerAddress, bool evenParity)
        {
            BitBang5Baud(controllerAddress, evenParity);

            Logger.WriteLine("Reading sync byte");
            var syncByte = _interface.ReadByte();
            if (syncByte != 0x55)
            {
                throw new InvalidOperationException(
                    $"Unexpected sync byte: Expected $55, Actual ${syncByte:X2}");
            }

            var keywordLsb = _interface.ReadByte();
            Logger.WriteLine($"Keyword Lsb ${keywordLsb:X2}");

            var keywordMsb = ReadAndAckByte();
            Logger.WriteLine($"Keyword Msb ${keywordMsb:X2}");

            var protocolVersion = ((keywordMsb & 0x7F) << 7) + (keywordLsb & 0x7F);
            Logger.WriteLine($"Protocol is KW {protocolVersion} (8N1)");

            if (protocolVersion >= 2000)
            {
                ReadComplement(controllerAddress);
            }

            return protocolVersion;
        }

        public byte ReadByte()
        {
            return _interface.ReadByte();
        }

        public void WriteByte(byte b)
        {
            WriteByteAndDiscardEcho(b);
        }

        public byte ReadAndAckByte()
        {
            var b = _interface.ReadByte();
            WriteComplement(b);
            return b;
        }

        public void ReadComplement(byte b)
        {
            var expectedComplement = (byte)~b;
            var actualComplement = _interface.ReadByte();
            if (actualComplement != expectedComplement)
            {
                throw new InvalidOperationException(
                    $"Received complement ${actualComplement:X2} but expected ${expectedComplement:X2}");
            }
        }

        private void WriteComplement(byte b)
        {
            var complement = (byte)~b;
            WriteByteAndDiscardEcho(complement);
        }

        /// <summary>
        /// Send a byte at 5 baud manually to the interface. The byte will be sent as
        /// 1 start bit, 7 data bits, 1 parity bit (even or odd), 1 stop bit.
        /// https://www.blafusel.de/obd/obd2_kw1281.html
        /// </summary>
        /// <param name="b">The byte to send.</param>
        /// <param name="evenParity">
        /// False for odd parity (KWP1281), true for even parity (KWP2000).</param>
        private void BitBang5Baud(byte b, bool evenParity)
        {
            // Disable garbage collection int this time-critical method
            bool noGc = GC.TryStartNoGCRegion(1024 * 1024);

            const int bitsPerSec = 5;
            long ticksPerBit = Stopwatch.Frequency / bitsPerSec;

            var stopWatch = new Stopwatch();
            long maxTick = 0;

            // Delay the appropriate amount and then set/clear the TxD line
            void BitBang(bool bit)
            {
                while (stopWatch.ElapsedTicks < maxTick)
                    ;
                if (bit)
                {
                    _interface.SetBreakOff();
                }
                else
                {
                    _interface.SetBreakOn();
                }

                maxTick += ticksPerBit;
            }

            bool parity = !evenParity; // XORed with each bit to calculate parity bit

            stopWatch.Start();

            BitBang(false); // Start bit

            for (int i = 0; i < 7; i++)
            {
                bool bit = (b & 1) == 1;
                parity ^= bit;
                b >>= 1;

                BitBang(bit);
            }

            BitBang(parity);

            BitBang(true); // Stop bit

            if (noGc)
            {
                GC.EndNoGCRegion();
            }

            // Throw away anything that might be in the receive buffer
            _interface.ClearReceiveBuffer();
        }


        /// <summary>
        /// Write a byte to the interface and read/discard its echo.
        /// </summary>
        private void WriteByteAndDiscardEcho(byte b)
        {
            _interface.WriteByteRaw(b);
            var echo = _interface.ReadByte();
            if (echo != b)
            {
                throw new InvalidOperationException($"Wrote 0x{b:X2} to port but echo was 0x{echo:X2}");
            }
        }

        private readonly IInterface _interface;

        public KwpCommon(IInterface @interface)
        {
            _interface = @interface;
        }
    }
}
