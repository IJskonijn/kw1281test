global using static BitFab.KW1281Test.Program;

using BitFab.KW1281Test.Interface;
using BitFab.KW1281Test.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;

[assembly: InternalsVisibleTo("BitFab.KW1281Test.Tests")]

namespace BitFab.KW1281Test
{
    class Program
    {
        public static ILog Log { get; private set; } = new ConsoleLog();

        static void Main(string[] args)
        {
            try
            {
                Log = new FileLog("KW1281Test.log");

                var tester = new Program();
                tester.Run(args);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Caught: {ex.GetType()} {ex.Message}");
                Log.WriteLine($"Unhandled exception: {ex}");
            }
            finally
            {
                Log.Close();
            }
        }

        private void Run(string[] args)
        {
            // Initial Setup
            DisplayInitialInfo(args);

            if (args.Length < 4)
            {
                if (args.Length == 2 && args[0].Contains("mileage", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(args[0], nameof(Utils.MileageHexToDecimal), StringComparison.InvariantCultureIgnoreCase))
                    {
                        Log.WriteLine($"Output mileage in decimal: {Utils.MileageHexToDecimal(args[1])}");
                    }
                    else if (string.Equals(args[0], nameof(Utils.MileageDecimalToHex), StringComparison.InvariantCultureIgnoreCase))
                    {
                        Log.WriteLine($"Output mileage in hex: {Utils.MileageDecimalToHex(int.Parse(args[1]))}");
                    }

                    // Rest of the program needs more than 2 arguments, so return here
                    return;
                }

                ShowUsage();
                return;
            }

            // Try setting the process priority
            TrySetRealTimeProcessPriority();

            string portName = args[0];
            int baudRate = int.Parse(args[1]);
            int controllerAddress = int.Parse(args[2], NumberStyles.HexNumber);
            string command = args[3];

            // Parse command-specific arguments
            var (address, length, value, softwareCoding, workshopCode, addressValuePairs, channel, channelValue, login, groupNumber, filename) = ParseCommandArguments(command, args);

            using var @interface = OpenPort(portName, baudRate);
            var tester = new Tester(@interface, controllerAddress);

            // Execute Command
            if (!ExecuteCommand(command.ToLower(), tester, address, length, value, softwareCoding, workshopCode, addressValuePairs, channel, channelValue, login, groupNumber, filename))
            {
                ShowUsage();
            }

            tester.EndCommunication();
        }

        private void DisplayInitialInfo(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("KW1281Test: Yesterday's diagnostics...");
            Thread.Sleep(2000);
            Console.WriteLine("Today.");
            Thread.Sleep(2000);
            Console.ResetColor();
            Console.WriteLine();

            var version = GetType().GetTypeInfo().Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
                .InformationalVersion;
            Log.WriteLine($"Version {version} (https://github.com/gmenounos/kw1281test/releases)");
            Log.WriteLine($"Args: {string.Join(' ', args)}");
            Log.WriteLine($"OSVersion: {Environment.OSVersion}");
            Log.WriteLine($".NET Version: {Environment.Version}");
            Log.WriteLine($"Culture: {CultureInfo.InstalledUICulture}");
        }

        private static void TrySetRealTimeProcessPriority()
        {
            try
            {
                // This seems to increase the accuracy of our timing loops
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
            }
            catch (Win32Exception)
            {
                // Ignore if we don't have permission to increase our priority
            }
        }

        private static CommandArguments ParseCommandArguments(string command, string[] args)
        {
            uint address = 0, length = 0;
            int softwareCoding = 0, workshopCode = 0;
            byte channel = 0, groupNumber = 0, value = 0;
            ushort channelValue = 0;
            ushort? login = null;
            var addressValuePairs = new List<KeyValuePair<ushort, byte>>();
            string? filename = null;

            // Handle command-specific arguments
            switch (command.ToLower())
            {
                case "readeeprom":
                case "readram":
                case "readrom":
                    address = Utils.ParseUint(args[4]);
                    break;

                case "dumprbxmem":
                case "dumprbxmemodd":
                case "dumpmem":
                case "dumpccmrom":
                case "dumpeeprom":
                    address = Utils.ParseUint(args[4]);
                    length = Utils.ParseUint(args[5]);
                    filename = args.Length > 6 ? args[6] : null;
                    break;

                case "writeeeprom":
                    address = Utils.ParseUint(args[4]);
                    value = (byte)Utils.ParseUint(args[5]);
                    break;

                case "setsoftwarecoding":
                    softwareCoding = (int)Utils.ParseUint(args[4]);
                    workshopCode = (int)Utils.ParseUint(args[5]);
                    break;

                case "adaptationread":
                case "adaptationsave":
                case "adaptationtest":
                    channel = byte.Parse(args[4]);
                    if (args.Length > 5)
                        login = ushort.Parse(args[5]);
                    break;

                case "basicsetting":
                case "groupread":
                    groupNumber = byte.Parse(args[4]);
                    break;

                case "writeedc15eeprom":
                    var dateString = DateTime.Now.ToString("s").Replace(':', '-');
                    filename = $"EDC15_EEPROM_{dateString}.bin";
                    if (!ParseAddressesAndValues(args.Skip(4).ToList(), out addressValuePairs))
                        ShowUsage();
                    break;

                case "findlogins":
                    login = ushort.Parse(args[4]);
                    break;

                case "loadEeprom":
                    address = Utils.ParseUint(args[4]);
                    filename = args[5];
                    break;

                    // Add more cases as needed for other commands
            }

            return new CommandArguments(address, length, value, softwareCoding, workshopCode, addressValuePairs, channel, channelValue, login, groupNumber, filename);
        }

        private static bool ExecuteCommand(string command, Tester tester, uint address, uint length, byte value, int softwareCoding, int workshopCode, List<KeyValuePair<ushort, byte>> addressValuePairs, byte channel, ushort channelValue, ushort? login, byte groupNumber, string filename)
        {
            switch (command)
            {
                case "dumprbxmem":
                    tester.DumpRBxMem(address, length, filename);
                    return true;

                case "dumprbxmemodd":
                    tester.DumpRBxMem(address, length, filename, evenParityWakeup: false);
                    return true;

                case "writeeeprom":
                    tester.WriteEeprom(address, value);
                    return true;

                case "writeedc15eeprom":
                    tester.ReadWriteEdc15Eeprom(filename, addressValuePairs);
                    return true;

                case "adaptationread":
                    tester.AdaptationRead(channel, login, tester.Kwp1281Wakeup().WorkshopCode);
                    return true;

                case "adaptationsave":
                    tester.AdaptationSave(channel, channelValue, login, tester.Kwp1281Wakeup().WorkshopCode);
                    return true;

                case "adaptationtest":
                    tester.AdaptationTest(channel, channelValue, login, tester.Kwp1281Wakeup().WorkshopCode);
                    return true;

                case "basicsetting":
                    tester.BasicSettingRead(groupNumber);
                    return true;

                case "readeeprom":
                    tester.ReadEeprom(address);
                    return true;

                case "readram":
                    tester.ReadRam(address);
                    return true;

                case "readrom":
                    tester.ReadRom(address);
                    return true;

                case "clearfaultcodes":
                    tester.ClearFaultCodes();
                    return true;

                case "getskc":
                    tester.GetSkc();
                    return true;

                case "getclusterid":
                    tester.GetClusterId();
                    return true;

                case "loadeeprom":
                    tester.LoadEeprom(address, filename);
                    return true;

                case "findlogins":
                    if (login.HasValue)
                        tester.FindLogins(login.Value, tester.Kwp1281Wakeup().WorkshopCode);
                    return true;

                case "dumpeeprom":
                    tester.DumpEeprom(address, length, filename);
                    return true;

                case "dumpccmrom":
                    tester.DumpCcmRom(filename);
                    return true;

                case "dumpmem":
                    tester.DumpMem(address, length, filename);
                    return true;

                case "dumpclusternecrom":
                    tester.DumpClusterNecRom(filename);
                    return true;

                case "readfaultcodes":
                    tester.ReadFaultCodes();
                    return true;

                case "readident":
                    tester.ReadIdent();
                    return true;

                case "reset":
                    tester.Reset();
                    return true;

                case "setsoftwarecoding":
                    tester.SetSoftwareCoding(softwareCoding, workshopCode);
                    return true;

                case "mapeeprom":
                    tester.MapEeprom(filename);
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Accept a series of string values in the format:
        /// ADDRESS1 VALUE1 [ADDRESS2 VALUE2 ... ADDRESSn VALUEn]
        ///     ADDRESS = EEPROM address in decimal (0-511) or hex ($00-$1FF)
        ///     VALUE = Value to be stored at address in decimal (0-255) or hex ($00-$FF)
        /// </summary>
        internal static bool ParseAddressesAndValues(
            List<string> addressesAndValues,
            out List<KeyValuePair<ushort, byte>> addressValuePairs)
        {
            addressValuePairs = [];

            if (addressesAndValues.Count % 2 != 0)
                return false;

            for (var i = 0; i < addressesAndValues.Count; i += 2)
            {
                uint address;
                var valueToParse = addressesAndValues[i];
                try
                {
                    address = Utils.ParseUint(valueToParse);
                }
                catch (Exception)
                {
                    Log.WriteLine($"Invalid address (bad format): {valueToParse}.");
                    return false;
                }

                if (address > 0x1FF)
                {
                    Log.WriteLine($"Invalid address (too large): {valueToParse}.");
                    return false;
                }

                uint value;
                valueToParse = addressesAndValues[i + 1];
                try
                {
                    value = Utils.ParseUint(valueToParse);
                }
                catch (Exception)
                {
                    Log.WriteLine($"Invalid value (bad format): {valueToParse}.");
                    return false;
                }

                if (value > 0xFF)
                {
                    Log.WriteLine($"Invalid value (too large): {valueToParse}.");
                    return false;
                }

                addressValuePairs.Add(new KeyValuePair<ushort, byte>((ushort)address, (byte)value));
            }

            return true;
        }

        /// <summary>
        /// Opens the serial port.
        /// </summary>
        /// <param name="portName">
        /// Either the device name of a serial port (e.g. COM1, /dev/tty23)
        /// or an FTDI USB->Serial device serial number (2 letters followed by 6 letters/numbers).
        /// </param>
        /// <param name="baudRate"></param>
        /// <returns></returns>
        private static IInterface OpenPort(string portName, int baudRate)
        {
            if (Regex.IsMatch(portName.ToUpper(), @"\A[A-Z0-9]{8}\Z"))
            {
                Log.WriteLine($"Opening FTDI serial port {portName}");
                return new FtdiInterface(portName, baudRate);
            }
            else
            {
                Log.WriteLine($"Opening serial port {portName}");
                return new GenericInterface(portName, baudRate);
            }
        }

        private static void mileageUtils()
        {
            // Std location for mileage in VWK501: starting from 0xFC
            // Std location for mileage in VWK503: starting from 0x13A
            // Location can vary depending on the cluster type

            // Test data:
            // 0x49, 0xE8,   0x49, 0xE8,   0x49, 0xE8,   0x49, 0xE8,   0x49, 0xE8,   0x49, 0xE8,   0x4A, 0xE8,   0x4A, 0xE8
            // VAG eeprom programmer 1.19g says: 97117
            var test2 = Utils.MileageHexToDecimal("49e8 49e8 49e8 49e8 49e8 49e8 4ae8 4ae8");

            // 0x8A, 0xBA, 0x8A, 0xBA, 0x8A, 0xBA, 0x8A, 0xBA, 0x8B, 0xBA, 0x8B, 0xBA, 0x8B, 0xBA, 0x8B, 0xBA,
            // VAG eeprom programmer 1.19g says: 284489
            var test3 = Utils.MileageHexToDecimal("8ABA 8ABA 8ABA 8ABA 8BBA 8BBA 8BBA 8BBA");
            var test4 = Utils.MileageHexToDecimal("8A BA 8A BA 8A BA 8A BA 8B BA 8B BA 8B BA 8B BA");

            var hex = Utils.MileageDecimalToHex(284488);
            var dec = Utils.MileageHexToDecimal(hex);

            var test = Utils.MileageHexToDecimal("fdff fdff feff feff feff feff feff feff");
        }

        private static void ShowUsage()
        {
            Log.WriteLine(@"
Usage: KW1281Test PORT BAUD ADDRESS COMMAND [args]
    PORT = COM1|COM2|etc.
    BAUD = 10400|9600|etc.
    ADDRESS = The controller address, e.g. 1 (ECU), 17 (cluster), 46 (CCM), 56 (radio)
    COMMAND =
        ActuatorTest
        AdaptationRead CHANNEL [LOGIN]
            CHANNEL = Channel number (0-99)
            LOGIN = Optional login (0-65535)
        AdaptationSave CHANNEL VALUE [LOGIN]
            CHANNEL = Channel number (0-99)
            VALUE = Channel value (0-65535)
            LOGIN = Optional login (0-65535)
        AdaptationTest CHANNEL VALUE [LOGIN]
            CHANNEL = Channel number (0-99)
            VALUE = Channel value (0-65535)
            LOGIN = Optional login (0-65535)
        BasicSetting GROUP
            GROUP = Group number (0-255)
            (Group 0: Raw controller data)
        ClarionVWPremium4SafeCode
        ClearFaultCodes
        DelcoVWPremium5SafeCode
        DumpEdc15Eeprom [FILENAME]
            FILENAME = Optional filename
        DumpEeprom START LENGTH [FILENAME]
            START = Start address in decimal (e.g. 0) or hex (e.g. $0)
            LENGTH = Number of bytes in decimal (e.g. 2048) or hex (e.g. $800)
            FILENAME = Optional filename
        DumpMarelliMem START LENGTH [FILENAME]
            START = Start address in decimal (e.g. 3072) or hex (e.g. $C00)
            LENGTH = Number of bytes in decimal (e.g. 1024) or hex (e.g. $400)
            FILENAME = Optional filename
        DumpMem START LENGTH [FILENAME]
            START = Start address in decimal (e.g. 8192) or hex (e.g. $2000)
            LENGTH = Number of bytes in decimal (e.g. 65536) or hex (e.g. $10000)
            FILENAME = Optional filename
        DumpRam START LENGTH [FILENAME]
            START = Start address in decimal (e.g. 8192) or hex (e.g. $2000)
            LENGTH = Number of bytes in decimal (e.g. 65536) or hex (e.g. $10000)
            FILENAME = Optional filename
        DumpRBxMem START LENGTH [FILENAME]
            START = Start address in decimal (e.g. 66560) or hex (e.g. $10400)
            LENGTH = Number of bytes in decimal (e.g. 1024) or hex (e.g. $400)
            FILENAME = Optional filename
        FindLogins LOGIN
            LOGIN = Known good login (0-65535)
        GetSKC
        GroupRead GROUP
            GROUP = Group number (0-255)
            (Group 0: Raw controller data)
        LoadEeprom START FILENAME
            START = Start address in decimal (e.g. 0) or hex (e.g. $0)
            FILENAME = Name of file containing binary data to load into EEPROM
        MapEeprom
        ReadFaultCodes
        ReadIdent
        ReadEeprom ADDRESS
            ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. $1109)
        ReadRAM ADDRESS
            ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. $1109)
        ReadROM ADDRESS
            ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. $1109)
        ReadSoftwareVersion
        Reset
        SetSoftwareCoding CODING WORKSHOP
            CODING = Software coding in decimal (e.g. 4361) or hex (e.g. $1109)
            WORKSHOP = Workshop code in decimal (e.g. 4361) or hex (e.g. $1109)
        ToggleRB4Mode
        WriteEdc15Eeprom ADDRESS1 VALUE1 [ADDRESS2 VALUE2 ... ADDRESSn VALUEn]
            ADDRESS = EEPROM address in decimal (0-511) or hex ($00-$1FF)
            VALUE = Value to be stored at address in decimal (0-255) or hex ($00-$FF)
        WriteEeprom ADDRESS VALUE
            ADDRESS = Address in decimal (e.g. 4361) or hex (e.g. $1109)
            VALUE = Value in decimal (e.g. 138) or hex (e.g. $8A)");

            Log.WriteLine(@"
Usage special mileage utils: KW1281Test COMMAND [arg]
    COMMAND =
        MileageDecimalToHex MILEAGE
            MILEAGE = Odometer value in decimal (e.g. 123456)
        MileageHexToDecimal MILEAGE
            MILEAGE = Odometer value in hex (e.g. FFFF FFFF FFFF FFFF FFFF FFFF FFFF FFFF)");
        }
    }
}
