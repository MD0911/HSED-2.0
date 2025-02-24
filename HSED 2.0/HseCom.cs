using Avalonia.Controls.Platform;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSED_2_0
{
    public class HseCom
    {
        
        public static byte CalculateCRC(byte[] data)
        {
            byte crc = 0x00;
            foreach (byte b in data)
            {
                crc ^= b;
            }
            return (byte)~crc;
        }

        public static int[] IntToArray(int number)
        {
            // Konvertiere die Zahl in einen String, um jede Ziffer einzeln zu verarbeiten
            string numberString = number.ToString();

            // Erstelle ein Array mit der Länge der Anzahl der Ziffern
            int[] digits = new int[numberString.Length];

            // Zerlege die Zeichenkette in einzelne Ziffern und speichere sie im Array
            for (int i = 0; i < numberString.Length; i++)
            {
                // Konvertiere jedes Zeichen zu einem int und speichere es im Array
                digits[i] = int.Parse(numberString[i].ToString());
            }

            return digits;
        }

        public static byte[] SendHseCommand(byte[] data)
        {
            try
            {
                using (SerialPort serialPort = new SerialPort("COM3", 38400, Parity.None, 8, StopBits.One))
                {
                    serialPort.ReadTimeout = 100;
                    serialPort.NewLine = "\r\n"; // Optional, falls das Gerät eine Endung sendet
                    Debug.WriteLine("Öffne serielle Verbindung...");
                    serialPort.Open();
                    Debug.WriteLine("Serielle Verbindung erfolgreich geöffnet.");

                    byte[] command = new byte[data.Length + 6];
                    command[0] = 0x95;
                    command[1] = 0x9A;
                    command[2] = 0x00;
                    command[3] = (byte)(data.Length + 6);
                    Array.Copy(data, 0, command, 4, data.Length);
                    command[data.Length + 4] = CalculateCRC(data);
                    command[data.Length + 5] = 0x85;

                    Debug.WriteLine("Zu sendendes Telegramm:");
                    Debug.WriteLine(BitConverter.ToString(command).Replace("-", " "));

                    serialPort.Write(command, 0, command.Length);
                    Debug.WriteLine("Befehl gesendet, warte auf Antwort...");

                    // Empfang des Antwort-Telegramms mit einer Schleife
                    byte[] buffer = new byte[1024];
                    int totalBytesRead = 0;

                    try
                    {
                        int bytesRead;
                        do
                        {
                            bytesRead = serialPort.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
                            totalBytesRead += bytesRead;
                        }
                        while (bytesRead > 0 && totalBytesRead < buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        Debug.WriteLine("Lesen der Daten aufgrund eines Zeitüberschreitungsfehlers abgebrochen.");
                    }

                    if (totalBytesRead > 0)
                    {
                        byte[] receivedData = new byte[totalBytesRead];
                        Array.Copy(buffer, receivedData, totalBytesRead);
                        return receivedData;
                    }
                    else
                    {
                        Debug.WriteLine("Keine Daten empfangen.");
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Fehler: {ex.Message}");
                Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                return null;
            }
        }

        public static int SendHse(int Art)
        {
            /* 
        Dokumentation:
            PARA_READ_REQ:
                Etagenanzahl        => 1001 
                Derzeitige Etage    => 1002
                Sicherheitskreis    => 1003:
                    0=> Nicht geschlossen
                    1=> Geschlossen
                    Aufbau der Rückgabewert:
                        SK1 SK2 SK3 SK4
                         X   X   X   X

                    Also: 0000 => Kein Sicherheitskreis geschlossen
                          0001 => Sicherheitskreis 4 geschlossen
                          0010 => Sicherheitskreis 3 geschlossen
                          0011 => Sicherheitskreis 3 und 4 geschlossen
                          0100 => Sicherheitskreis 2 geschlossen
                          0101 => Sicherheitskreis 2 und 4 geschlossen
                          0110 => Sicherheitskreis 2 und 3 geschlossen
                          0111 => Sicherheitskreis 2, 3 und 4 geschlossen
                          1000 => Sicherheitskreis 1 geschlossen
                          1001 => Sicherheitskreis 1 und 4 geschlossen
                          1010 => Sicherheitskreis 1 und 3 geschlossen
                          1011 => Sicherheitskreis 1, 3 und 4 geschlossen
                          1100 => Sicherheitskreis 1 und 2 geschlossen
                          1101 => Sicherheitskreis 1, 2 und 4 geschlossen
                          1110 => Sicherheitskreis 1, 2 und 3 geschlossen
                          1111 => Alle Sicherheitskreise geschlossen

                Echte Etagenanzahel   => 1004
                        (ohen -1... 0 ist die unterste)
                Aufzugszustand      => 1005
                Türenzustand        => 1006

            READ_DATE_REQ:
                Datum und Uhrzeit   => 2001

            READ_TEMP_REQ:
                Datum und Uhrzeit   => 3001
             */

            if (Art == 1001)
            {
                try
                {
                    byte[] floor = new byte[12];
                    floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                    int bootFloor = floor[10];
                    floor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x01, 0x00, 0x03 });
                    int topFloor = floor[10];

                    return (topFloor - bootFloor) + 1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }

            }

            if (Art == 1002)
            {
                try
                {
                    byte[] bottomfloor = new byte[12];
                    byte[] currentfloor = new byte[12];
                    bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    int IcurrentFloor = currentfloor[10];
                    IcurrentFloor = IcurrentFloor + bootFloor;
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }


            }



            if (Art == 1003)
            {
                try
                {
                    // Empfange das Byte-Array von SendHseCommand
                    byte[] inputByte = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x02, 0x00, 0x05 });
                    byte sk = inputByte[10]; // Byte für die Sicherheitskreiszustände

                    // Array zur Speicherung der Bits als bool (true = 1, false = 0)
                    bool[] Sk = new bool[4];

                    // Extrahiere die ersten 4 Bits und speichere sie in Sk
                    for (int i = 0; i < 4; i++)
                    {
                        Sk[i] = (sk & (1 << i)) != 0;
                    }

                    // Berechne den Integer-Wert, wobei SK1 die Tausenderstelle ist und SK4 die Einerstelle
                    int result = (Sk[0] ? 1000 : 0) + (Sk[1] ? 100 : 0) + (Sk[2] ? 10 : 0) + (Sk[3] ? 1 : 0);

                    // Ausgabe des Ergebnisses
                    Console.WriteLine($"Kombinierter Wert von SK1 bis SK4: {result}");

                    return result; // Erfolgreiche Ausführung
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }
            }

            if (Art == 1004)
            {
                try
                {
                    byte[] bottomfloor = new byte[12];
                    byte[] currentfloor = new byte[12];
                    bottomfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x07, 0x01, 0x03 });
                    int topFloor = bottomfloor[10];
                    byte[] bottomfloorName = new byte[2];
                    bottomfloorName[0] = bottomfloor[11];
                    bottomfloorName[1] = bottomfloor[10];
                    string asciiString = Encoding.ASCII.GetString(bottomfloorName);
                    int bootFloor = Convert.ToInt32(asciiString);
                    currentfloor = SendHseCommand(new byte[] { 0x03, 0x01, 0x21, 0x01, 0x01, 0x05 });
                    int IcurrentFloor = currentfloor[10];
                    return IcurrentFloor;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }


            }

            if (Art == 1005)
            {
                try
                {
                    byte[] zustand = new byte[12];
                    zustand = SendHseCommand(new byte[] { 0x03, 0x01, 0x20, 0xFF, 0x00, 0x05 });
                   
                    return zustand[10];
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }

            }

            if (Art == 1006)
            {
                try
                {
                    byte[] tuer1 = new byte[13];
                    tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x01, 0x06 });
                    int zustand1 = tuer1[11];
                 
                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }

            }

            if (Art == 1016)
            {
                try
                {
                    byte[] tuer1 = new byte[13];
                    tuer1 = SendHseCommand(new byte[] { 0x03, 0x01, 0x63, 0x01, 0x02, 0x06 });
                    int zustand1 = tuer1[11];

                    return zustand1;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Fehler: {ex.Message}");
                    Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                    return 505;
                }

            }



            if (Art == 2001)
                {
                    //nicht funktions fähig
                    try
                    {
                        byte[] date = new byte[12];
                        date = SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x00, 0x00, 0x03 });
                        int year = date[10];
                        int month = date[11];
                        int day = date[12];
                        int hour = date[13];
                        int minute = date[14];
                        int second = date[15];
                        return 0;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Fehler: {ex.Message}");
                        Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                        return 505;
                    }
                }

                if (Art == 3001)
                {

                    try
                    {
                        byte[] date = new byte[12];
                        date = SendHseCommand(new byte[] { 0x0C, 0x03, 0x26, 0x48, 0x00, 0x02 });
                        int temp = date[10];
                        return temp;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Fehler: {ex.Message}");
                        Debug.WriteLine($"Stack Trace: {ex.StackTrace}");
                        return 505;
                    }
                }

                return 404;

            }
        }
    }

