using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Globalization;
using HtmlAgilityPack;

public class Urnik
{
    private static readonly HttpClient client = new HttpClient();

    private class Predmet
    {
        public string Dan = "";
        public string Zacetek = "";
        public string Konec = "";
        public string Ime = "";
        public string Profesor = "";
        public string Ucilnica = "";
        public int ZacetnaUra;
        public int KoncnaUra;
    }

    private class Celica
    {
        public HtmlNode Node;
        public int Stolpec;
        public int Vrstica;
        public int Colspan;
        public int Rowspan;

        public Celica(HtmlNode node, int stolpec, int vrstica, int colspan, int rowspan)
        {
            Node = node;
            Stolpec = stolpec;
            Vrstica = vrstica;
            Colspan = colspan;
            Rowspan = rowspan;
        }
    }

    public static void GetUrnik()
    {
        Console.OutputEncoding = Encoding.UTF8;
        GetUrnikAsync().GetAwaiter().GetResult();
    }

    private static async Task GetUrnikAsync()
    {
        int teden = ISOWeek.GetWeekOfYear(DateTime.Now);
        string url = $"https://urnik.visja.si/{teden}/c/c00006.htm";

        string[] dnevi =
        {
            "Ponedeljek",
            "Torek",
            "Sreda",
            "Četrtek",
            "Petek"
        };

        string[] casi =
        {
            "8:00-8:45",
            "8:50-9:35",
            "9:40-10:25",
            "10:30-11:15",
            "11:20-12:05",
            "12:10-12:55",
            "13:00-13:45",
            "13:50-14:35",
            "14:40-15:25",
            "16:00-16:45",
            "16:50-17:35",
            "17:40-18:25",
            "18:30-19:15",
            "19:20-20:05"
        };

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            byte[] bytes = await client.GetByteArrayAsync(url);
            string html = Encoding.GetEncoding(1250).GetString(bytes);

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);

            var tabele = doc.DocumentNode.SelectNodes("//table");

            HtmlNode? tabela = null;

            if (tabele != null)
            {
                foreach (var t in tabele)
                {
                    var vrsticeTabele = t.SelectNodes("./tr");

                    if (vrsticeTabele == null)
                        continue;

                    bool imaDneve = false;

                    foreach (var vrstica in vrsticeTabele)
                    {
                        string besedilo = HtmlEntity.DeEntitize(vrstica.InnerText);

                        if (besedilo.Contains("Ponedeljek") &&
                            besedilo.Contains("Torek"))
                        {
                            imaDneve = true;
                            break;
                        }
                    }

                    if (imaDneve)
                    {
                        tabela = t;
                        break;
                    }
                }
            }

            if (tabela == null)
            {
                Console.WriteLine("Napaka: glavne tabele urnika ni bilo mogoče najti.");
                return;
            }

            var vrstice = tabela.SelectNodes("./tr");

            if (vrstice == null)
            {
                Console.WriteLine("Napaka: vrstice urnika niso bile najdene.");
                return;
            }

            List<Celica> celiceUrnika = new List<Celica>();

            // Vsaka vrstica HTML ima lahko celice, ki segajo
            // tudi v naslednje vrstice zaradi rowspan.
            Dictionary<int, int> zasedeniStolpci = new Dictionary<int, int>();

            for (int r = 0; r < vrstice.Count; r++)
            {
                var celice = vrstice[r].SelectNodes("./td|./th");

                if (celice == null)
                {
                    ZmanjsajZasedenost(zasedeniStolpci);
                    continue;
                }

                int stolpec = 0;

                foreach (var celica in celice)
                {
                    while (zasedeniStolpci.ContainsKey(stolpec) &&
                           zasedeniStolpci[stolpec] > 0)
                    {
                        stolpec++;
                    }

                    int colspan = 1;
                    int rowspan = 1;

                    int.TryParse(celica.GetAttributeValue("colspan", "1"), out colspan);
                    int.TryParse(celica.GetAttributeValue("rowspan", "1"), out rowspan);

                    if (colspan < 1)
                        colspan = 1;

                    if (rowspan < 1)
                        rowspan = 1;

                    celiceUrnika.Add(new Celica(
                        celica,
                        stolpec,
                        r,
                        colspan,
                        rowspan
                    ));

                    for (int c = stolpec; c < stolpec + colspan; c++)
                    {
                        if (rowspan > 1)
                            zasedeniStolpci[c] = rowspan;
                    }

                    stolpec += colspan;
                }

                ZmanjsajZasedenost(zasedeniStolpci);
            }

            // Poišči vrstice, ki predstavljajo začetek šolske ure.
            Dictionary<int, int> urePoVrsticah = new Dictionary<int, int>();

            foreach (var celica in celiceUrnika)
            {
                if (celica.Stolpec != 0)
                    continue;

                string besedilo = HtmlEntity.DeEntitize(celica.Node.InnerText);

                Match cas = Regex.Match(
                    besedilo,
                    @"(\d{1,2}:\d{2})\s*-\s*(\d{1,2}:\d{2})"
                );

                if (!cas.Success)
                    continue;

                int indeksUre = -1;

                for (int i = 0; i < casi.Length; i++)
                {
                    if (casi[i] == cas.Groups[1].Value + "-" + cas.Groups[2].Value)
                    {
                        indeksUre = i + 1;
                        break;
                    }
                }

                if (indeksUre != -1)
                    urePoVrsticah[celica.Vrstica] = indeksUre;
            }

            List<Predmet> predmeti = new List<Predmet>();

            foreach (var celica in celiceUrnika)
            {
                if (celica.Stolpec == 0)
                    continue;

                int indeksDneva = (celica.Stolpec - 1) / 12;

                if (indeksDneva < 0 || indeksDneva >= dnevi.Length)
                    continue;

                // Celice s predmeti vsebujejo notranjo tabelo.
                var notranjeCelice = celica.Node.SelectNodes(".//table//tr[1]/td");

                if (notranjeCelice == null || notranjeCelice.Count < 3)
                    continue;

                string ime = HtmlEntity.DeEntitize(
                    notranjeCelice[0].InnerText
                ).Trim();

                string profesor = HtmlEntity.DeEntitize(
                    notranjeCelice[1].InnerText
                ).Trim();

                string ucilnica = HtmlEntity.DeEntitize(
                    notranjeCelice[2].InnerText
                ).Trim();

                ime = Regex.Replace(ime, @"\s+", " ");
                profesor = Regex.Replace(profesor, @"\s+", " ");
                ucilnica = Regex.Replace(ucilnica, @"\s+", " ");

                if (string.IsNullOrWhiteSpace(ime))
                    continue;

                // Poišči šolsko uro, ki se začne v isti vrstici.
                if (!urePoVrsticah.TryGetValue(celica.Vrstica, out int zacetnaUra))
                    continue;

                int trajanje = Math.Max(1, celica.Rowspan / 2);

                int koncnaUra = Math.Min(casi.Length, zacetnaUra + trajanje - 1);

                string[] zacetniCas = casi[zacetnaUra - 1].Split('-');
                string[] koncniCas = casi[koncnaUra - 1].Split('-');

                predmeti.Add(new Predmet
                {
                    Dan = dnevi[indeksDneva],
                    ZacetnaUra = zacetnaUra,
                    KoncnaUra = koncnaUra,
                    Zacetek = zacetniCas[0],
                    Konec = koncniCas[1],
                    Ime = ime,
                    Profesor = profesor,
                    Ucilnica = ucilnica
                });
            }

            Console.WriteLine();
            Console.WriteLine("╭══════════════════════════════════════════════╮");
            Console.WriteLine($"              URNIK | TEDEN {teden}");
            Console.WriteLine("╰══════════════════════════════════════════════╯");

            foreach (string dan in dnevi)
            {
                Console.WriteLine();
                Console.WriteLine($"  {dan.ToUpper()}");
                Console.WriteLine("  ────────────────────────────────────────────");

                bool najden = false;

                foreach (var predmet in predmeti)
                {
                    if (predmet.Dan != dan)
                        continue;

                    najden = true;

                    Console.WriteLine($"  Ura       : {predmet.Zacetek} - {predmet.Konec}");
                    Console.WriteLine($"  Predmet   : {predmet.Ime}");
                    Console.WriteLine($"  Profesor  : {predmet.Profesor}");
                    Console.WriteLine($"  Učilnica  : {predmet.Ucilnica}");
                    Console.WriteLine("  ────────────────────────────────────────────");
                }

                if (!najden)
                    Console.WriteLine("  Ta dan nimaš pouka.");
            }

            Console.WriteLine();
            Console.WriteLine("════════════ KONEC URNIKA ════════════");
            Console.WriteLine();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Napaka pri pridobivanju urnika: {ex.Message}");
        }
    }

    private static void ZmanjsajZasedenost(Dictionary<int, int> zasedenost)
    {
        List<int> stolpci = new List<int>(zasedenost.Keys);

        foreach (int stolpec in stolpci)
        {
            zasedenost[stolpec]--;

            if (zasedenost[stolpec] <= 0)
                zasedenost.Remove(stolpec);
        }
    }
}
