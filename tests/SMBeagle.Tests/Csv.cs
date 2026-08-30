namespace SMBeagle.Tests;

/// <summary>
/// Contrat CSV partagé avec le parseur docia (src/docia/ingest/smbeagle_csv.py) :
/// 19 colonnes dans cet ordre, chaînes entre guillemets (guillemet interne doublé),
/// DateTime / bool / long / enum nus.
/// </summary>
static class Csv
{
    public static readonly string[] Header =
    {
        "Name", "Host", "Extension", "Username", "Hostname", "UNCDirectory",
        "CreationTime", "LastWriteTime", "Readable", "Writeable", "Deletable",
        "DirectoryType", "Base", "FileSize", "AccessTime", "FileAttributes",
        "Owner", "FastHash", "FileSignature",
    };

    public static readonly HashSet<int> QuotedColumns = new() { 0, 1, 2, 3, 4, 5, 12, 15, 16, 17, 18 };
    public static readonly HashSet<int> UnquotedColumns = new() { 6, 7, 8, 9, 10, 11, 13, 14 };

    /// <summary>Découpe une ligne RFC 4180 (guillemets doublés) en champs, valeurs décodées.</summary>
    public static List<string> Split(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else inQuotes = false;
                }
                else current.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }
        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>Pour chaque champ, indique s'il commence par un guillemet (sans décoder).</summary>
    public static List<bool> QuotedFlags(string line)
    {
        var flags = new List<bool>();
        int i = 0;
        while (i <= line.Length)
        {
            bool quoted = i < line.Length && line[i] == '"';
            flags.Add(quoted);
            if (quoted)
            {
                i++;
                while (i < line.Length)
                {
                    if (line[i] == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                while (i < line.Length && line[i] != ',') i++;
            }
            else
            {
                int end = line.IndexOf(',', i);
                i = end == -1 ? line.Length : end;
            }
            if (i < line.Length && line[i] == ',') { i++; continue; }
            break;
        }
        return flags;
    }

    public static List<Dictionary<string, string>> ReadRows(string path)
    {
        var lines = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToList();
        Assert.True(lines.Count >= 1, "CSV vide");
        var header = Split(lines[0]);
        Assert.Equal(Header, header);
        var rows = new List<Dictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var fields = Split(line);
            Assert.Equal(Header.Length, fields.Count);
            rows.Add(Header.Zip(fields).ToDictionary(p => p.First, p => p.Second));
        }
        return rows;
    }
}
