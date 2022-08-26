﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace BitFab.KW1281Test.Cluster
{
    class MarelliCluster : ICluster
    {
        public void UnlockForEepromReadWrite()
        {
            // Nothing to do
        }

        public string DumpEeprom(uint? address, uint? length, string? dumpFileName)
        {
            address ??= GetDefaultAddress();
            dumpFileName ??= $"marelli_mem_${address:X4}.bin";

            DumpMem(dumpFileName, (ushort)address, (ushort?)length);

            return dumpFileName;
        }

        private ushort GetDefaultAddress()
        {
            if (_ecuInfo.Contains("1C0920901"))
            {
                return 3072; // $0C00
            }
            else if (
                _ecuInfo.Contains("1C0920921") ||
                _ecuInfo.Contains("1C0920951") ||
                _ecuInfo.Contains("8N2920930") ||
                _ecuInfo.Contains("8N2920980"))
            {
                return 14336; // $3800
            }
            else
            {
                throw new InvalidOperationException("Unsupported cluster version.");
            }
        }

        /// <summary>
        /// Dumps memory from a Marelli cluster to a file.
        /// </summary>
        private byte[] DumpMem(
            string filename,
            ushort address,
            ushort? count = null)
        {
            byte entryH; // High byte of code entry point
            byte regBlockH; // High byte of register block

            if (_ecuInfo.Contains("1C0920901"))
            {
                // Tested:
                // Beetle 1C0920901C M73 V07

                entryH = 0x02; // $0200
                regBlockH = 0x08; // $0800
                count ??= 1024; // $0400
            }
            else if (
                _ecuInfo.Contains("1C0920921") || // Beetle 1C0920951A
                _ecuInfo.Contains("1C0920951") || // Beetle 1C0920921G
                _ecuInfo.Contains("8N2920930") || // Audi TT 8N2920980A
                _ecuInfo.Contains("8N2920980"))    // Audi TT 8N2920930C)
            {
                // Tested:
                // Beetle 1C0920921G M73 V08
                // Beetle 1C0920951A M73 V02
                // Audi TT 8N2920930C M73 D55
                // Audi TT 8N2920980A M73 D14

                entryH = 0x18; // $1800
                regBlockH = 0x20; // $2000
                count ??= 2048; // $0800
            }
            else if (address == 3072 && count == 1024)
            {
                Log.WriteLine("Untested cluster version! You may need to disconnect your battery if this fails.");

                entryH = 0x02;
                regBlockH = 0x08;
            }
            else if (address == 14336 && count == 2048)
            {
                Log.WriteLine("Untested cluster version! You may need to disconnect your battery if this fails.");

                entryH = 0x18;
                regBlockH = 0x20;
            }
            else
            {
                Log.WriteLine("Unsupported cluster software version");
                return Array.Empty<byte>();
            }

            Log.WriteLine("Sending block 0x6C");
            _kwp1281.SendBlock(new List<byte> { 0x6C });

            Thread.Sleep(250);

            Log.WriteLine("Writing data to cluster microcontroller");
            var data = new byte[]
            {
                0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x50, 0x50, 0x34,
                entryH, 0x00, // Entry point $xx00
            };
            if (!WriteMarelliBlockAndReadAck(data))
            {
                return Array.Empty<byte>();
            }

            // Now we write a small memory dump program to the 68HC12 processor

            Log.WriteLine("Writing memory dump program to cluster microcontroller");

            var startH = (byte)(address / 256);
            var startL = (byte)(address % 256);

            var end = address + count;
            var endH = (byte)(end / 256);
            var endL = (byte)(end % 256);

            var program = new byte[]
            {
                entryH, 0x00, // Address $xx00 

                0x14, 0x50,                     // orcc #$50
                0x07, 0x32,                     // bsr FeedWatchdog

                // Set baud rate to 9600
                0xC7,                           // clrb
                0x7B, regBlockH, 0xC8,          // stab $xxC8   ; SC1BDH
                0xC6, 0x34,                     // ldab #$34
                0x7B, regBlockH, 0xC9,          // stab $xxC9   ; SC1BDL

                // Enable transmit, disable UART interrupts
                0xC6, 0x08,                     // ldab #$08
                0x7B, regBlockH, 0xCB,          // stab $xxCB   ; SC1CR2

                0xCE, startH, startL,           // ldx #start
                // SendLoop:
                0xA6, 0x30,                     // ldaa 1,X+
                0x07, 0x0F,                     // bsr SendByte
                0x8E, endH, endL,               // cpx #end
                0x26, 0xF7,                     // bne SendLoop
                // Poison the watchdog to force a reboot
                0xCC, 0x11, 0x11,               // ldd #$1111
                0x7B, regBlockH, 0x17,          // stab $xx17   ; COPRST
                0x7A, regBlockH, 0x17,          // staa $xx17   ; COPRST
                0x3D,                           // rts

                // SendByte:
                0xF6, regBlockH, 0xCC,          // ldab $xxCC   ; SC1SR1
                0x7A, regBlockH, 0xCF,          // staa $xxCF   ; SC1DRL
                // TxBusy:
                0x07, 0x06,                     // bsr FeedWatchdog
                // Loop until TC (Transmit Complete) bit is set
                0x1F, regBlockH, 0xCC, 0x40, 0xF9,   // brclr $xxCC,$40,TxBusy   ; SC1SR1
                0x3D,                           // rts

                // FeedWatchdog:
                0xCC, 0x55, 0xAA,               // ldd #$55AA
                0x7B, regBlockH, 0x17,          // stab $xx17   ; COPRST
                0x7A, regBlockH, 0x17,          // staa $xx17   ; COPRST
                0x3D,                           // rts
            };
            if (!WriteMarelliBlockAndReadAck(program))
            {
                return Array.Empty<byte>();
            }

            Log.WriteLine("Receiving memory dump");

            var kwpCommon = _kwp1281.KwpCommon;
            var mem = new List<byte>();
            for (int i = 0; i < count; i++)
            {
                var b = kwpCommon.ReadByte();
                mem.Add(b);
            }

            File.WriteAllBytes(filename, mem.ToArray());
            Log.WriteLine($"Saved memory dump to {filename}");

            Log.WriteLine("Done");

            _kwp1281.SetDisconnected(); // Don't try to send EndCommunication block

            return mem.ToArray();
        }

        private bool WriteMarelliBlockAndReadAck(byte[] data)
        {
            var kwpCommon = _kwp1281.KwpCommon;

            var count = (ushort)(data.Length + 2); // Count includes 2-byte checksum
            var countH = (byte)(count / 256);
            var countL = (byte)(count % 256);
            kwpCommon.WriteByte(countH);
            kwpCommon.WriteByte(countL);

            var sum = (ushort)(countH + countL);
            foreach (var b in data)
            {
                kwpCommon.WriteByte(b);
                sum += b;
            }
            kwpCommon.WriteByte((byte)(sum / 256));
            kwpCommon.WriteByte((byte)(sum % 256));

            var expectedAck = new byte[] { 0x03, 0x09, 0x00, 0x0C };

            Log.WriteLine("Receiving ACK");
            var ack = new List<byte>();
            for (int i = 0; i < 4; i++)
            {
                var b = kwpCommon.ReadByte();
                ack.Add(b);
            }
            if (!ack.SequenceEqual(expectedAck))
            {
                Log.WriteLine($"Expected ACK but received {Utils.Dump(ack)}");
                return false;
            }

            return true;
        }

        private readonly IKW1281Dialog _kwp1281;
        private readonly string _ecuInfo;

        public MarelliCluster(IKW1281Dialog kwp1281, string ecuInfo)
        {
            _kwp1281 = kwp1281;
            _ecuInfo = ecuInfo;
        }
    }
}
