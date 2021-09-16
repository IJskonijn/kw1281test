﻿using BitFab.KW1281Test.Interface;
using System;
using System.Diagnostics;
using System.Threading;

namespace BitFab.KW1281Test
{
    public interface IKwpCommon
    {
        IInterface Interface { get; }

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
        public IInterface Interface { get; }

        public int WakeUp(byte controllerAddress, bool evenParity)
        {
            // Disable garbage collection int this time-critical method
            bool noGc = GC.TryStartNoGCRegion(1024 * 1024);

            byte syncByte = 0;
            const int maxTries = 3;
            for (int i = 1; i <= maxTries; i++)
            {
                BitBang5Baud(controllerAddress, evenParity);

                // Throw away anything that might be in the receive buffer
                Interface.ClearReceiveBuffer();

                Log.WriteLine("Reading sync byte");
                try
                {
                    syncByte = Interface.ReadByte();
                    break;
                }
                catch (TimeoutException)
                {
                    if (i < maxTries)
                    {
                        Log.WriteLine("Retrying wakeup message...");
                    }
                    else
                    {
                        throw new InvalidOperationException("Controller did not wake up.");
                    }
                }
            }

            if (noGc)
            {
                GC.EndNoGCRegion();
            }

            if (syncByte != 0x55)
            {
                throw new InvalidOperationException(
                    $"Unexpected sync byte: Expected $55, Actual ${syncByte:X2}");
            }

            var keywordLsb = Interface.ReadByte();
            Log.WriteLine($"Keyword Lsb ${keywordLsb:X2}");

            var keywordMsb = ReadByte();
            Log.WriteLine($"Keyword Msb ${keywordMsb:X2}");

            Thread.Sleep(25);
            WriteComplement(keywordMsb);

            var protocolVersion = ((keywordMsb & 0x7F) << 7) + (keywordLsb & 0x7F);
            Log.WriteLine($"Protocol is KW {protocolVersion} (8N1)");

            if (protocolVersion >= 2000)
            {
                ReadComplement(controllerAddress);
            }

            return protocolVersion;
        }

        public byte ReadByte()
        {
            return Interface.ReadByte();
        }

        public void WriteByte(byte b)
        {
            WriteByteAndDiscardEcho(b);
        }

        public byte ReadAndAckByte()
        {
            var b = Interface.ReadByte();
            WriteComplement(b);
            return b;
        }

        public void ReadComplement(byte b)
        {
            var expectedComplement = (byte)~b;
            var actualComplement = Interface.ReadByte();
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
            const int bitsPerSec = 5;
            long ticksPerBit = Stopwatch.Frequency / bitsPerSec;

            long maxTick;

            // Delay the appropriate amount and then set/clear the TxD line
            void BitBang(bool bit)
            {
                while (Stopwatch.GetTimestamp() < maxTick)
                    ;
                if (bit)
                {
                    Interface.SetBreakOff();
                }
                else
                {
                    Interface.SetBreakOn();
                }

                maxTick += ticksPerBit;
            }

            bool parity = !evenParity; // XORed with each bit to calculate parity bit

            maxTick = Stopwatch.GetTimestamp();
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

            // Wait for end of stop bit
            while (Stopwatch.GetTimestamp() < maxTick)
                ;
        }

        /// <summary>
        /// Write a byte to the interface and read/discard its echo.
        /// </summary>
        private void WriteByteAndDiscardEcho(byte b)
        {
            Interface.WriteByteRaw(b);
            var echo = Interface.ReadByte();
            if (echo != b)
            {
                throw new InvalidOperationException($"Wrote 0x{b:X2} to port but echo was 0x{echo:X2}");
            }
        }

        public KwpCommon(IInterface @interface)
        {
            Interface = @interface;
        }
    }
}
