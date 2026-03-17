using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HSED_2_0
{
    /// <summary>
    /// Verwaltet eine serielle Verbindung als Singleton – die Schnittstelle wird beim ersten Zugriff initialisiert.
    /// Eine öffentliche Open-Methode erlaubt es, die Verbindung manuell zu öffnen.
    /// </summary>
   

    public class HseCom
    {
        private static int ReadNumericParameter(byte indexHigh, byte indexLow, params byte[] preferredDataTypes)
        {
            foreach (byte dataType in preferredDataTypes)
            {
                byte[] response = SendHseCommand(new byte[] { 0x03, 0x01, indexHigh, indexLow, 0x00, dataType });
                if (response == null || response.Length <= 10)
                    continue;

                string hex = BitConverter.ToString(response);
                Debug.WriteLine($"Parameter {indexHigh:X2}{indexLow:X2} Antwort: {hex}");

                if (response.Length <= 9)
                    continue;

                byte responseDataType = response[9];
                int valueOffset = 10;

                try
                {
                    switch (responseDataType)
                    {
                        case DataTypes.D_UNSIGNED8:
                            return response[valueOffset];

                        case DataTypes.D_INTEGER16:
                            if (response.Length <= valueOffset + 1)
                                continue;
                            return BitConverter.ToInt16(new byte[] { response[valueOffset], response[valueOffset + 1] }, 0);

                        case DataTypes.D_UNSIGNED16:
                            if (response.Length <= valueOffset + 1)
                                continue;
                            return BitConverter.ToUInt16(new byte[] { response[valueOffset], response[valueOffset + 1] }, 0);

                        case DataTypes.D_UNSIGNED32:
                        case DataTypes.D_IDENTITY:
                            if (response.Length <= valueOffset + 3)
                                continue;
                            uint value32 = BitConverter.ToUInt32(
                                new byte[] { response[valueOffset], response[valueOffset + 1], response[valueOffset + 2], response[valueOffset + 3] }, 0);

                            if (value32 > int.MaxValue)
                            {
                                Debug.WriteLine($"Parameter {indexHigh:X2}{indexLow:X2} überschreitet Int32: {value32}");
                                return int.MaxValue;
                            }

                            return (int)value32;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler beim Interpretieren des Parameters {indexHigh:X2}{indexLow:X2}: {ex.Message}");
                }
            }

            byte[] legacyResponse = SendHseCommand(new byte[] { 0x03, 0x01, indexHigh, indexLow });
            if (legacyResponse != null && legacyResponse.Length > 9)
            {
                string legacyHex = BitConverter.ToString(legacyResponse);
                Debug.WriteLine($"Legacy-Parameter {indexHigh:X2}{indexLow:X2} Antwort: {legacyHex}");

                if (legacyResponse.Length > 10)
                {
                    return BitConverter.ToUInt16(new byte[] { legacyResponse[8], legacyResponse[9] }, 0);
                }
            }

            return 505;
        }

        /// <summary>
        /// Berechnet die CRC (Prüfsumme) für die Daten.
        /// </summary>
        public static byte CalculateCRC(byte[] data)
        {
            byte crc = 0x00;
            foreach (byte b in data)
            {
                crc ^= b;
            }
            return (byte)~crc;
        }

        /// <summary>
        /// Wandelt eine Zahl in ein Array von Ziffern um.
        /// </summary>
        public static int[] IntToArray(int number)
        {
            // Formatiert die Zahl immer als 4-stelliger String, z.B. "0000" bei 0.
            string numberString = number.ToString("D4");
            int[] digits = new int[numberString.Length];
            for (int i = 0; i < numberString.Length; i++)
            {
                digits[i] = int.Parse(numberString[i].ToString());
            }
            return digits;
        }

        /// <summary>
        /// Sendet ein HSE-Telegramm über die persistente serielle Verbindung.
        /// </summary>
        public static byte[] SendHseCommand(byte[] data)
        {
            return SerialPortManager.Instance.SendCommand(data);
        }

        public static int SendHse(int Art)
        {
            // Art 1001: Berechnung der Etagenanzahl
            if (Art == 1001)
            {
                try
                {
                    byte[] floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    if (floor == null || floor.Length <= 10)
                        return 505;
                    int bootFloor = floor[10];
                    floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x01, 0x00, 0x03 });
                    if (floor == null || floor.Length <= 10)
                        return 505;
                    int topFloor = floor[10];
                    return (topFloor - bootFloor) + 1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1002: Berechnung der aktuellen Etage aus Boot- und aktueller Etage
            if (Art == 1002)
            {
                try
                {
                    byte[] bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    if (bottomfloor == null || bottomfloor.Length <= 11)
                        return 505;
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    byte[] currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    if (currentfloor == null || currentfloor.Length <= 10)
                        return 505;
                    int IcurrentFloor = currentfloor[10];
                    IcurrentFloor += bootFloor;
                    Debug.WriteLine("Einzelabfrage Etage: " + IcurrentFloor);
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1002): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1003: Kombination von SK1 bis SK4
            if (Art == 1003)
            {
                try
                {
                    byte[] inputByte = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
                    if (inputByte == null || inputByte.Length <= 10)
                        return 505;
                    byte sk = inputByte[10];
                    bool[] Sk = new bool[4];
                    for (int i = 0; i < 4; i++)
                    {
                        Sk[i] = (sk & (1 << i)) != 0;
                    }
                    int result = (Sk[0] ? 1000 : 0) + (Sk[1] ? 100 : 0) + (Sk[2] ? 10 : 0) + (Sk[3] ? 1 : 0);
                    Debug.WriteLine($"Kombinierter Wert von SK1 bis SK4: {result}");
                    return result;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1003): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1004: Aktuelle Etage (ohne Bootaddition)
            if (Art == 1004)
            {
                try
                {
                    byte[] bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    if (bottomfloor == null || bottomfloor.Length <= 11)
                        return 505;
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    byte[] currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    if (currentfloor == null || currentfloor.Length <= 10)
                        return 505;
                    int IcurrentFloor = currentfloor[10];
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1004): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1005: Zustand (z. B. Fehlerzustand) auslesen
            if (Art == 1005)
            {
                try
                {
                    byte[] zustand = SendHseCommand(new byte[] { 0x03, 0x01, 0x20, 0xFF, 0x00, 0x05 });
                    int Zustand = zustand[10];
                    Debug.WriteLine("Zustand: " + Zustand);
                    if (zustand == null || zustand.Length <= 10)
                        return 505;
                    return zustand[10];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1005): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1006: Tür 1 Zustand
            if (Art == 1006)
            {
                try
                {
                    byte[] tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x01, 0x06 });
                    if (tuer1 == null || tuer1.Length <= 11)
                        return 505;
                    int zustand1 = tuer1[11];
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1006): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 1016: Tür 1 Zustand alternative Abfrage
            if (Art == 1016)
            {
                try
                {
                    byte[] tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x02, 0x06 });
                    if (tuer1 == null || tuer1.Length <= 11)
                        return 505;
                    int zustand1 = tuer1[11];
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (1016): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 2001: Datum/Uhrzeit abfragen
            if (Art == 2001)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    if (date == null || date.Length <= 15)
                        return 505;
                    int year = date[10];
                    int month = date[11];
                    int day = date[12];
                    int hour = date[13];
                    int minute = date[14];
                    int second = date[15];
                    // Hier könnte man auch das Datum zusammenbauen. Fürs Beispiel wird 0 zurückgegeben.
                    return 0;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (2001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 3001: Temperatur abfragen
            if (Art == 3001)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x0C, 0x03, 0x26, 0x48, 0x00, 0x02 });
                    if (date == null || date.Length <= 10)
                        return 505;
                    int temp = date[10];
                    return temp;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }
            

            // Art 8002: Monetoring

            if (Art == 8002)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x05, 0x01 });
                    if (date == null || date.Length <= 10)
                        return 505;
                    int temp = date[10];
                    return temp;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 2045)
            {
                try
                {
                    return ReadNumericParameter(0x21, 0x62, DataTypes.D_UNSIGNED32, DataTypes.D_UNSIGNED16, DataTypes.D_INTEGER16);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 2145)
            {
                try
                {
                    return ReadNumericParameter(0x21, 0x61, DataTypes.D_UNSIGNED32, DataTypes.D_UNSIGNED16, DataTypes.D_INTEGER16);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 2653)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x03, 0x01, 0x26, 0x053, 0x00, 0x05 });
                    if (date == null || date.Length <= 10)
                        return 505;
                    int temp = date[10];
                    Debug.WriteLine("Gesamtes Telegramm VFANG: " + BitConverter.ToString(date));
                    Debug.WriteLine("VFANG EINZELAbfrage: " + temp);
                    return temp;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }

            // Art 10101010: Pos_Calc
            if (Art == 10101010)
            {
                try
                {
                    byte[] date = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x3D });
                    string Hex = BitConverter.ToString(date);
                    Debug.WriteLine("Hex: " + Hex);
                    if (date == null || date.Length <= 10)
                        return 505;
                    float temp = BitConverter.ToSingle(new byte[] { date[10], date[11], date[12], date[13] });
                    int rund = Convert.ToInt32(temp);
                    Debug.WriteLine("Pos_Calc: " + temp);
                    return rund;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler (3001): {ex.Message}\n{ex.StackTrace}");
                    return 505;
                }
            }


            return 404;
        }
    }
}
