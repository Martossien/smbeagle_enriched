using CommandLine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace SMBeagle.Output
{
    /// <summary>
    /// Manifeste JSON écrit en fin de scan (--manifest) : version, horodatages,
    /// options effectives, cibles, compteurs, chemin du CSV et liste des colonnes.
    /// </summary>
    public sealed class ScanManifest
    {
        static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;
        public List<string> Targets { get; } = new();

        /// <summary>
        /// Cibles demandées mais **non scannées** (accès refusé, montage cassé).
        /// Vide dans le cas normal. Non vide, le scan sort en
        /// <see cref="SMBeagle.ExitCodes.PartialScan"/> : le CSV est bon, le périmètre
        /// est incomplet, et l'aval doit pouvoir le dire à l'utilisateur au lieu de
        /// présenter l'audit comme exhaustif.
        /// </summary>
        public List<string> Skipped { get; } = new();
        /// <summary>Sous-répertoires du périmètre que l'énumération n'a pas pu lire (accès
        /// refusé, chemin trop long) : leurs fichiers manquent à l'inventaire. Liste bornée
        /// (<see cref="FileDiscovery.Directory.MAX_UNREADABLE_LISTED"/>), le compte est exact.</summary>
        public List<string> UnreadableDirectories { get; } = new();
        public long UnreadableDirectoryCount { get; set; }
        /// <summary>Fichiers sautés parce qu'illisibles en cours d'examen (disparus, refusés).</summary>
        public long UnreadableFileCount { get; set; }
        /// <summary>Jonctions et liens de répertoire ignorés (le contenu réel est scanné par son chemin).</summary>
        public long ReparsePointsSkipped { get; set; }
        public long Hosts { get; set; }
        public long Shares { get; set; }
        public long Files { get; set; }
        public string Csv { get; set; }

        /// <summary>Version de l'exécutable (Major.Minor.Build).</summary>
        public static string Version
        {
            get
            {
                Version v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        /// <summary>
        /// Toutes les options de la ligne de commande, clé = nom long, valeur
        /// effective (défauts compris). Le mot de passe est masqué.
        /// </summary>
        public static Dictionary<string, object> DescribeOptions(object options)
        {
            var result = new Dictionary<string, object>();
            foreach (PropertyInfo prop in options.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var attr = prop.GetCustomAttribute<OptionAttribute>();
                if (attr == null)
                    continue;
                string key = string.IsNullOrEmpty(attr.LongName) ? prop.Name : attr.LongName;
                object value = prop.GetValue(options);
                if (key == "password" && value != null)
                    value = "***";
                else if (value is string s)
                    value = s;
                else if (value is IEnumerable seq)
                    value = seq.Cast<object>().Select(o => o?.ToString()).ToList();
                result[key] = value;
            }
            return result;
        }

        public void Write(string path, object options)
        {
            var manifest = new Dictionary<string, object>
            {
                ["version"] = Version,
                ["started_at"] = StartedAt.ToString("o"),
                ["finished_at"] = DateTimeOffset.Now.ToString("o"),
                ["options"] = DescribeOptions(options),
                ["targets"] = Targets,
                ["skipped"] = Skipped,
                ["counts"] = new Dictionary<string, long>
                {
                    ["hosts"] = Hosts,
                    ["shares"] = Shares,
                    ["files"] = Files,
                    ["dirs_unreadable"] = UnreadableDirectoryCount,
                    ["files_unreadable"] = UnreadableFileCount,
                    ["reparse_points_skipped"] = ReparsePointsSkipped,
                },
                ["unreadable_directories"] = UnreadableDirectories,
                ["csv"] = Csv,
                ["columns"] = FileDiscovery.Output.FileOutput.Columns,
            };
            File.WriteAllText(path, JsonSerializer.Serialize(manifest, _json) + Environment.NewLine);
        }
    }
}
