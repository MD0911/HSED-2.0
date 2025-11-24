using HSED_2_0;
using System;
using System.Diagnostics;
using System.IO;
using System.Xml.Linq;

namespace HSED_2._0
{
    internal class LievViewManager
    {
        // Diese Eigenschaften werden aus dem MonetoringManager übernommen.
        public int BootFloor { get; private set; }
        public int TopFloor { get; private set; }
        public int GesamtFloor { get; private set; }
        public static int[] IngrementEtage { get; private set; }

        // Hier wird das zusammengesetzte SVG als String gespeichert.
        public string ComposedSvg { get; private set; }

        // Neu: Gesamt-Höhe des zusammengesetzten SVG (im Originalkoordinatensystem)
        public double TotalHeight { get; private set; }

        // Pfad zur SVG-Datei, die eine einzelne Etage darstellt.
        public string SingleFloorSvgPath { get; set; } = "Animation/forBuild/SchachtVorne.svg";
        public string AlternativeSingleFloorSvgPath { get; set; } = "Animation/forBuild/SchachtHinten.svg";

        public string SingleFloorSvgPathLetzte { get; set; } = "Animation/forBuild/SchachtVorneLetzte.svg";

        public string AlternativeSingleFloorSvgLetzte { get; set; } = "Animation/forBuild/SchachtHintenLetzte.svg";

        /// <summary>
        /// Liest die Floor-Werte aus dem MonetoringManager aus.
        /// </summary>
        public void Initialize()
        {
            // Übernehme die statischen Werte aus dem MonetoringManager
            BootFloor = MonetoringManager.BootFloor;
            TopFloor = MonetoringManager.TopFloor;
            GesamtFloor = MonetoringManager.RawGesamtFloor;
            SetIngrementEtage();

            Debug.WriteLine($"LievViewManager: BootFloor = {BootFloor}, TopFloor = {TopFloor}, GesamtFloor = {GesamtFloor}");
        }

        public void SetIngrementEtage()
        {
            int gesamtFloor = MonetoringManager.GesamtFloor;
            // Initialisieren des Arrays, falls nicht schon erfolgt.
            IngrementEtage = new int[gesamtFloor];

            for (int i = 1; i < gesamtFloor; i++)
            {
                byte[] increment = HseCom.SendHseCommand(new byte[] { 0x03, 0x01, 0x24, 0x29, (byte)i });

                if (increment == null || increment.Length < 10)
                {
                    Debug.WriteLine($"Ungültige Antwort für Etage {i}");
                    continue;
                }

                IngrementEtage[i] = BitConverter.ToInt32(new byte[] { increment[10], increment[11], increment[12], increment[13] });
                Debug.WriteLine($"Increment Etage {i}: {IngrementEtage[i]}");
            }
        }

        /// <summary>
        /// Baut aus dem Einzel-SVG für eine Etage ein zusammengesetztes SVG,
        /// in dem die Etagen übereinander angeordnet sind.
        /// </summary>
        public void BuildSchachtSvg()
        {
            // Lade das SVG der einzelnen Etage
            string floorSvgContent = File.ReadAllText(SingleFloorSvgPath);
            XDocument floorSvgDoc = XDocument.Parse(floorSvgContent);

            // Bestimme die Höhe der Etage (z.B. aus dem "height"-Attribut oder als Fallback 325)
            double floorHeight = GetFloorHeight(floorSvgDoc);
            // Gesamthöhe der Etagen
            double totalHeight = floorHeight * GesamtFloor;
            TotalHeight = totalHeight;  // Speichern der Gesamthöhe (nur Etagen)

            // Namespace definieren (SVG-Namespace)
            XNamespace svgNs = "http://www.w3.org/2000/svg";

            // Footer-SVG einlesen (gehört ganz nach unten)
            string footerSvgContent = File.ReadAllText(SingleFloorSvgPathLetzte);
            XDocument footerSvgDoc = XDocument.Parse(footerSvgContent);
            double footerHeight = GetFloorHeight(footerSvgDoc); // bei deinen neuen SVGs: 456

            // Root-SVG anlegen: Höhe = Etagen + Footer
            XElement composedSvg = new XElement(svgNs + "svg",
                new XAttribute("xmlns", svgNs.NamespaceName),
                new XAttribute("width", floorSvgDoc.Root.Attribute("width")?.Value ?? "auto"),
                new XAttribute("height", totalHeight + footerHeight)
            );

            // Etagen stapeln
            for (int i = 0; i < GesamtFloor; i++)
            {
                XElement group = new XElement(svgNs + "g",
                    new XAttribute("transform", $"translate(0, {i * floorHeight})")
                );

                foreach (XElement element in floorSvgDoc.Root.Elements())
                {
                    group.Add(new XElement(element));
                }

                composedSvg.Add(group);
            }

            // Footer direkt UNTER die Etagen setzen
            XElement footerGroup = new XElement(svgNs + "g",
                new XAttribute("transform", $"translate(0, {totalHeight})")
            );
            foreach (XElement element in footerSvgDoc.Root.Elements())
            {
                footerGroup.Add(new XElement(element));
            }
            composedSvg.Add(footerGroup);

            // Speichere das zusammengesetzte SVG als String.
            ComposedSvg = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), composedSvg).ToString();
        }


        /// <summary>
        /// Extrahiert die Höhe aus dem "height"-Attribut des SVG-Root-Elements.
        /// Falls nicht vorhanden, wird ein Fallback-Wert (325) verwendet.
        /// </summary>
        /// <param name="svgDoc">Das SVG-Dokument</param>
        /// <returns>Höhe als double</returns>
        private double GetFloorHeight(XDocument svgDoc)
        {
            string heightStr = svgDoc.Root.Attribute("height")?.Value;
            if (!string.IsNullOrWhiteSpace(heightStr))
            {
                heightStr = heightStr.Replace("px", "").Trim();
                if (double.TryParse(heightStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double height))
                {
                    return height;
                }
            }

            // Falls kein gültiges "height" vorhanden ist, verwende den Fallback-Wert.
            return 325;
        }

        /// <summary>
        /// Baut alternativ ein zusammengesetztes SVG anhand eines alternativen SVG-Pfads.
        /// </summary>
        public void BuildSchachtSvgAlternative()
        {
            string floorSvgContent = File.ReadAllText(AlternativeSingleFloorSvgPath);
            XDocument floorSvgDoc = XDocument.Parse(floorSvgContent);

            double floorHeight = GetFloorHeight(floorSvgDoc);
            double totalHeight = floorHeight * GesamtFloor;

            XNamespace svgNs = "http://www.w3.org/2000/svg";

            // Alternativen Footer laden
            string footerSvgContent = File.ReadAllText(AlternativeSingleFloorSvgLetzte);
            XDocument footerSvgDoc = XDocument.Parse(footerSvgContent);
            double footerHeight = GetFloorHeight(footerSvgDoc); // bei deinen neuen SVGs: 456

            // Root-SVG: Höhe = Etagen + Footer
            XElement composedSvg = new XElement(svgNs + "svg",
                new XAttribute("xmlns", svgNs.NamespaceName),
                new XAttribute("width", floorSvgDoc.Root.Attribute("width")?.Value ?? "auto"),
                new XAttribute("height", totalHeight + footerHeight)
            );

            // Etagen stapeln
            for (int i = 0; i < GesamtFloor; i++)
            {
                XElement group = new XElement(svgNs + "g",
                    new XAttribute("transform", $"translate(0, {i * floorHeight})")
                );

                foreach (XElement element in floorSvgDoc.Root.Elements())
                {
                    group.Add(new XElement(element));
                }

                composedSvg.Add(group);
            }

            // Footer direkt UNTER die Etagen setzen
            XElement footerGroup = new XElement(svgNs + "g",
                new XAttribute("transform", $"translate(0, {totalHeight})")
            );
            foreach (XElement element in footerSvgDoc.Root.Elements())
            {
                footerGroup.Add(new XElement(element));
            }
            composedSvg.Add(footerGroup);

            ComposedSvgAlternative = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), composedSvg).ToString();
        }



        // Neue Eigenschaft für das alternative zusammengesetzte SVG.
        public string ComposedSvgAlternative { get; private set; }

        /// <summary>
        /// Ruft alle nötigen Methoden in der richtigen Reihenfolge auf, um die Schachtansicht vorzubereiten.
        /// </summary>
        public void PrepareSchacht()
        {
            Initialize();
            BuildSchachtSvg();
            BuildSchachtSvgAlternative();

            // Optional: Speichere das zusammengesetzte SVG in eine Datei.
            //string outputPath = Path.Combine(AppContext.BaseDirectory, "Output", "SchachtComposed.svg");
           // File.WriteAllText(outputPath, ComposedSvg);
        }
    }
}
