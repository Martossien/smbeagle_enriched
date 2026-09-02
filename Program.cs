using SMBeagle.FileDiscovery;
using SMBeagle.HostDiscovery;
using SMBeagle.NetworkDiscovery;
using SMBeagle.Output;
using SMBeagle.ShareDiscovery;
using CommandLine;
using CommandLine.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Diagnostics.CodeAnalysis;

namespace SMBeagle
{
    class Program
    {
        [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Options))]
        static int Main(string[] args)
        {
            // Détecté avant l'analyse : une erreur d'arguments doit aussi sortir en JSON.
            bool progressJson = args.Contains("--progress-json");
            var parser = new Parser(with => with.HelpWriter = null);
            var parserResult = parser.ParseArguments<Options>(args);
            int code = ExitCodes.ArgumentError;
            parserResult
                .WithParsed(opts => code = SafeRun(opts))
                .WithNotParsed(errs => code = OutputHelp(parserResult, errs, progressJson));
            return code;
        }

        /// <summary>Exécute le scan ; toute exception devient un code 1 (et un événement JSON « error »).</summary>
        static int SafeRun(Options opts)
        {
            if (opts.ProgressJson)
            {
                OutputHelper.UseStderrForHumanOutput();
                ProgressReporter.Current = new ProgressReporter(Console.Out);
            }
            try
            {
                return Run(opts);
            }
            catch (Exception ex)
            {
                OutputHelper.WriteError($"{ex.GetType().Name} : {ex.Message}");
                ProgressReporter.Current?.Error(ex.Message);
                return ExitCodes.RuntimeError;
            }
            finally
            {
                ProgressReporter.Current?.Dispose();
                ProgressReporter.Current = null;
            }
        }

        /// <summary>
        /// Conseil commun aux arguments mal découpés : sous Windows, un chemin non
        /// guillemeté contenant une espace arrive en plusieurs argv (cmd.exe / MSVCRT).
        /// </summary>
        const string QuotingHint =
            "HINT: a path containing spaces must be quoted, e.g. --local-path \"D:\\my files\"\n" +
            "HINT: do not end a quoted path with a backslash: \"D:\\folder\\\" does not close the quote, write \"D:\\folder\" instead";

        /// <summary>Complément affiché quand un --local-path n'est pas pleinement qualifié.</summary>
        const string RelativeHint =
            "HINT: a relative path is resolved against the current directory, so it could silently scan the wrong folder";

        static string Quoted(IEnumerable<string> values)
            => string.Join(", ", values.Select(v => $"'{v}'"));

        static int Fail(int code, string message)
        {
            OutputHelper.WriteLine(message);
            ProgressReporter.Current?.Error(message);
            return code;
        }

        /// <summary>Rien à scanner ou rien trouvé : fin de scan normale (manifeste, « done ») puis code 3.</summary>
        static int NothingFound(Options opts, ScanManifest manifest, string message)
        {
            OutputHelper.WriteLine(message);
            Finish(opts, manifest);
            return ExitCodes.NothingFound;
        }

        /// <summary>Fin de scan commune : vidage des sorties, manifeste, événement « done ».</summary>
        static int Finish(Options opts, ScanManifest manifest)
        {
            ProgressReporter.Current?.Stage(ProgressReporter.STAGE_WRITING);
            OutputHelper.WriteLine("7. Completing the writes to CSV or elasticsearch (or both)");
            OutputHelper.CloseAndFlush();
            OutputHelper.WriteLine(" -- AUDIT COMPLETE --");
            manifest.UnreadableDirectoryCount = FileDiscovery.Directory.UnreadableDirectoryCount;
            manifest.UnreadableFileCount = FileDiscovery.Directory.UnreadableFileCount;
            manifest.UnreadableDirectories.AddRange(FileDiscovery.Directory.UnreadableDirectories);
            manifest.ReparsePointsSkipped = FileDiscovery.Directory.ReparsePointsSkipped;
            if (opts.ManifestPath != null)
                manifest.Write(opts.ManifestPath, opts);
            ProgressReporter.Current?.Done(manifest.Files, manifest.Csv);
            if (manifest.Files == 0)
                return ExitCodes.NothingFound;
            // Le périmètre amputé prime sur le succès : des fichiers ont bien été écrits,
            // mais l'appelant ne doit pas lire « 0 » et croire avoir tout vu.
            return manifest.Skipped.Count > 0 ? ExitCodes.PartialScan : ExitCodes.Ok;
        }

        /// <summary>Arguments validés : ce que <see cref="Run"/> utilise une fois toutes les gardes passées.</summary>
        sealed class ValidatedArguments
        {
            /// <summary>--local-path retenus, absolus et lisibles (les refusés en sont retirés).</summary>
            public List<string> LocalPaths { get; } = new();
            /// <summary>--local-path demandés mais **écartés** (accès refusé) : le périmètre
            /// scanné est plus petit que celui demandé. Reporté dans le manifeste
            /// (`skipped`) et dans le code de retour, faute de quoi la seule trace serait
            /// une ligne d'avertissement noyée dans la sortie du scanner.</summary>
            public List<string> SkippedPaths { get; } = new();
            /// <summary>Motifs de récupération (-g) effectifs : défaut amont ou --file-pattern validés.</summary>
            public List<string> FilePatterns { get; set; } = new();
            /// <summary>Vrai hors Windows, ou avec des identifiants passés en ligne de commande.</summary>
            public bool CrossPlatform { get; set; }
            /// <summary>Vrai dès qu'un --local-path a été fourni, même si aucun n'est exploitable.</summary>
            public bool LocalScan { get; set; }
        }

        /// <summary>État d'un --local-path constaté avant tout scan.</summary>
        enum LocalPathState { Ok, Empty, NotAbsolute, NotFound, AccessDenied }

        /// <summary>
        /// Classe un --local-path sans jamais le résoudre contre le répertoire courant.
        /// Directory.Exists rend false aussi bien pour un dossier absent que pour un dossier
        /// existant mais inaccessible : on énumère réellement pour distinguer les deux, sinon
        /// un partage fermé par ACL serait rapporté comme « introuvable ».
        /// </summary>
        static LocalPathState ClassifyLocalPath(string path, out string detail)
        {
            detail = "";
            if (string.IsNullOrWhiteSpace(path))
                return LocalPathState.Empty;
            try
            {
                // Un chemin relatif ('fichiers', '..\data', 'C:foo') serait résolu contre le
                // répertoire courant : c'est exactement ce qui fait scanner le mauvais dossier
                // quand un chemin non guillemeté est coupé en plusieurs argv par Windows.
                if (!System.IO.Path.IsPathFullyQualified(path))
                    return LocalPathState.NotAbsolute;
                _ = System.IO.Directory.EnumerateFileSystemEntries(path).FirstOrDefault();
                return LocalPathState.Ok;
            }
            catch (UnauthorizedAccessException)
            {
                return LocalPathState.AccessDenied;
            }
            catch (System.IO.DirectoryNotFoundException)
            {
                return LocalPathState.NotFound;
            }
            catch (Exception ex)
            {
                // Lecteur mappé déconnecté, partage injoignable, nom invalide : introuvable, motif à l'appui.
                detail = ex.Message;
                return LocalPathState.NotFound;
            }
        }

        /// <summary>
        /// Toutes les gardes d'arguments, avant le moindre effet de bord (manifeste, CSV, scan).
        /// Rend null si le scan peut démarrer, sinon le code de retour à propager.
        /// Un --local-path refusé n'est pas une erreur d'arguments : il est écarté avec un
        /// avertissement, comme le fait déjà FileFinder.GetLocalPathDirectories, et le scan
        /// continue sur les autres chemins. S'il n'en reste aucun, la fin de scan normale rend 3.
        /// </summary>
        static int? ValidateArguments(Options opts, out ValidatedArguments validated)
        {
            validated = new ValidatedArguments
            {
                LocalScan = opts.LocalPaths != null && opts.LocalPaths.Any(),
            };

            // Un argument surnuméraire signale presque toujours un chemin coupé par
            // l'absence de guillemets : mieux vaut échouer que de le jeter en silence.
            if (opts.ExtraArgs != null && opts.ExtraArgs.Any())
                return Fail(ExitCodes.ArgumentError,
                    $"ERROR: unexpected extra argument(s): {Quoted(opts.ExtraArgs)}\n{QuotingHint}");

            // Chaque --local-path doit être absolu ET exister AVANT le scan : sinon un chemin
            // coupé dont un fragment existe ferait scanner le mauvais dossier en code 0.
            if (validated.LocalScan)
            {
                List<string> problems = new(), denied = new();
                bool relative = false;
                foreach (string path in opts.LocalPaths)
                {
                    switch (ClassifyLocalPath(path, out string detail))
                    {
                        case LocalPathState.Ok:
                            validated.LocalPaths.Add(System.IO.Path.GetFullPath(path));
                            break;
                        case LocalPathState.Empty:
                            problems.Add("ERROR: --local-path needs a directory, an empty value was given");
                            break;
                        case LocalPathState.NotAbsolute:
                            relative = true;
                            problems.Add($"ERROR: --local-path is not an absolute path: '{path}'");
                            break;
                        case LocalPathState.AccessDenied:
                            denied.Add(path);
                            break;
                        default:
                            problems.Add($"ERROR: --local-path directory not found: '{path}'"
                                + (detail.Length == 0 ? "" : $" ({detail})"));
                            break;
                    }
                }
                if (problems.Count > 0)
                {
                    if (relative)
                        problems.Add(RelativeHint);
                    problems.Add(QuotingHint);
                    return Fail(ExitCodes.ArgumentError, string.Join("\n", problems));
                }
                foreach (string path in denied)
                    OutputHelper.WriteLine($"WARNING: --local-path access denied, directory skipped: '{path}'");
                validated.SkippedPaths.AddRange(denied);
            }

            // Un scan --local-path ne parle pas SMB : pas d'identifiants requis, même hors Windows.
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !validated.LocalScan)
            {
                if (opts.Username == null || opts.Password == null)
                    return Fail(ExitCodes.ArgumentError, "ERROR: Username and Password required on none Windows platforms");
            }

            if (opts.Username == null ^ opts.Password == null)
                return Fail(ExitCodes.ArgumentError, "ERROR: We need a username and password, not just one");

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || opts.Username != null)
            {
                validated.CrossPlatform = true;
                // The library we use hangs when scanning ourselves
                if (opts.ScanLocalShares)
                    return Fail(ExitCodes.ArgumentError, "ERROR: We cannot scan local shares when running on Linux or with commandline credentials");
            }

            if (opts.CsvFile == null && opts.ElasticsearchHost == null)
                return Fail(ExitCodes.ArgumentError, "ERROR: an output is required (-c csv file and/or -e elasticsearch host)");

            // Un -c dégénéré doit être rejeté ici : plus loin, Path.GetFullPath lèverait en code 1.
            if (opts.CsvFile != null && string.IsNullOrWhiteSpace(opts.CsvFile))
                return Fail(ExitCodes.ArgumentError, $"ERROR: -c/--csv-file needs a file name, an empty value was given\n{QuotingHint}");

            if (opts.Aggression < 1 || opts.Aggression > 10)
                return Fail(ExitCodes.ArgumentError, $"ERROR: Aggression should be between 1 and 10, not '{opts.Aggression}'");
            if (opts.FileWorkers < 1 || opts.FileWorkers > 64)
                return Fail(ExitCodes.ArgumentError, $"ERROR: --file-workers should be between 1 and 64, not '{opts.FileWorkers}'");

            // Motifs de récupération (-g) : valeur par défaut amont, ou ceux fournis, validés avant tout scan
            validated.FilePatterns = new List<string> { ".*(password|config|credentials|creds).*", ".*(ps1|bat|vbs|sh|cmd)$" };
            if (opts.GrabFiles && opts.FilePatterns.Any())
            {
                validated.FilePatterns = opts.FilePatterns.ToList();
                foreach (string pattern in validated.FilePatterns)
                {
                    try
                    {
                        _ = Regex.IsMatch("", pattern);
                    }
                    catch (ArgumentException)
                    {
                        return Fail(ExitCodes.ArgumentError, $"ERROR: Provided regex pattern '{pattern}' is invalid");
                    }
                }
            }

            return null;
        }

        static int Run(Options opts)
        {
            if (!opts.Quiet)
                OutputHelper.ConsoleWriteLogo();
            else
                OutputHelper.WriteLine("SMBeagle by PunkSecurity [punksecurity.co.uk]");

            // Toutes les gardes d'arguments d'abord : rien ne doit être créé ni résolu avant.
            int? argumentError = ValidateArguments(opts, out ValidatedArguments args);
            if (argumentError.HasValue)
                return argumentError.Value;

            bool localScan = args.LocalScan;
            bool crossPlatform = args.CrossPlatform;
            List<string> filePatterns = args.FilePatterns;

            ScanManifest manifest = new();
            manifest.Skipped.AddRange(args.SkippedPaths);
            if (opts.CsvFile != null)
                manifest.Csv = System.IO.Path.GetFullPath(opts.CsvFile);

            Host.PORT_MAX_WAIT_MS = 1010 - (100 * opts.Aggression);

            String username = "";
            if (opts.Username != null)
                username = opts.Username;
            if (opts.Domain != "")
                username = $"{opts.Domain}\\{username}";

            if (opts.ElasticsearchHost != null && opts.ElasticsearchPort != null)
                OutputHelper.EnableElasticsearchLogging($"http://{opts.ElasticsearchHost}:{opts.ElasticsearchPort}/", username);

            if (opts.CsvFile != null)
                OutputHelper.EnableCSVLogging(opts.CsvFile, username);

            if (opts.GrabFiles)
            {
                OutputHelper.WriteLine($"We will grab files and store them in {opts.OutputDirectory} directory");
                if (opts.FilePatterns.Any())
                    OutputHelper.WriteLine($"Using the provided regexes", 1);
            }
            else if (!opts.Quiet)
            {
                OutputHelper.WriteLine($"Will NOT Grab files - rerun and use the '-g' flag to grab them if needed");
            }

            // Handle local path scanning
            if (localScan)
            {
                ProgressReporter.Current?.Stage(ProgressReporter.STAGE_FILES);
                OutputHelper.WriteLine("Performing local directory scan as --local-path is specified...");
                if (opts.Networks.Any() || opts.Hosts.Any() || opts.ScanLocalShares)
                    OutputHelper.WriteLine("WARNING: --local-path is mutually exclusive with network options. Network options ignored.", 1);

                // Tous les chemins refusés : fin de scan normale (CSV, manifeste, « done ») en code 3.
                if (args.LocalPaths.Count == 0)
                    return NothingFound(opts, manifest, "ERROR: No valid local path to scan");

                FileFinder ffLocal = new(BuildScanOptions(opts, new List<Share>(), filePatterns, crossPlatform, args.LocalPaths));
                manifest.Targets.AddRange(ffLocal.RootPaths);
                manifest.Files = ffLocal.FileCount;
                if (manifest.Targets.Count == 0)
                    return NothingFound(opts, manifest, "ERROR: No valid local path to scan");
                return Finish(opts, manifest);
            }

            ProgressReporter.Current?.Stage(ProgressReporter.STAGE_DISCOVERY);
            NetworkFinder
                nf = new();

            // Discover networks automagically
            if (!opts.DisableNetworkDiscovery)
            {
                OutputHelper.WriteLine("1. Performing network discovery...");
                nf.DiscoverNetworks();

                OutputHelper.WriteLine($"discovered {nf.PrivateNetworks.Count} private networks and {nf.PrivateAddresses.Count} private addresses", 1);

                if (!opts.Quiet)
                {
                    OutputHelper.WriteLine("private networks:", 2);
                    foreach (Network pn in nf.PrivateNetworks)
                        OutputHelper.WriteLine(pn.ToString(), 3);
                    OutputHelper.WriteLine("private addresses:", 2);
                    foreach (string pa in nf.PrivateAddresses)
                        OutputHelper.WriteLine(pa.ToString(), 3);
                }

                if (opts.Verbose)
                {
                    OutputHelper.WriteLine($"discovered but will ignore the following {nf.PublicAddresses.Count} public addresses:", 1);
                    foreach (string pa in nf.PublicAddresses)
                        OutputHelper.WriteLine(pa, 2);
                    OutputHelper.WriteLine($"discovered but will ignore the following {nf.PublicNetworks.Count} public networks:", 1);
                    foreach (Network pn in nf.PublicNetworks)
                        OutputHelper.WriteLine(pn.ToString(), 2);
                }
            }

            else
            {
                OutputHelper.WriteLine("1. Skipping network discovery due to -D switch...");
            }

            // build list of provided exclusions
            List<string> filteredAddresses = new();
            List<Network> networks = new();

            if (!opts.DisableNetworkDiscovery)
            {
                OutputHelper.WriteLine("2. Filtering discovered networks and addresses...");

                // build list of discovered and provided networks
                Int16
                    maxNetworkSizeForScanning = Int16.Parse(opts.MaxNetworkSizeForScanning);

                networks = nf.PrivateNetworks
                    .Where(item => item.IPVersion == 4) // We cannot scan ipv6 networks, they are HUGE, but we do scan the ipv6 hosts
                    .Where(item => Int16.Parse(item.Cidr) >= maxNetworkSizeForScanning)
                    .Where(item => !opts.ExcludedNetworks.Contains(item.ToString()))
                    .ToList();

                OutputHelper.WriteLine($"filtered and have {networks.Count} private networks to scan and {filteredAddresses.Count} private addresses to exclude", 1);

                if (!opts.Quiet)
                {
                    if (networks.Count > 0)
                    {
                        OutputHelper.WriteLine("private networks to scan:", 2);
                        foreach (Network pn in networks)
                            OutputHelper.WriteLine(pn.ToString(), 3);
                    }


                    if (filteredAddresses.Count > 0)
                    {
                        OutputHelper.WriteLine("private addresses to exclude:", 2);
                        foreach (string pa in filteredAddresses)
                            OutputHelper.WriteLine(pa, 3);
                    }
                }
            }
            else
            {
                OutputHelper.WriteLine("2. Skipping filtering as network discovery disabled...");
            }

            if (!opts.ScanLocalShares)
            {
                filteredAddresses.AddRange(nf.DiscoverNetworksViaClientConfiguration(store: false));
            }
            filteredAddresses.AddRange(opts.ExcludedHosts.ToList());

            List<string> addresses = new();

            if (opts.Networks.Any() || opts.Hosts.Any())
            {
                OutputHelper.WriteLine("3. Processing manual networks and addresses...");
                foreach (string network in opts.Networks)
                {
                    networks.Add(
                        new Network(network, Enums.NetworkDiscoverySourceEnum.ARGS)
                        );
                    OutputHelper.WriteLine($"added network '{network}'", 1);

                }

                foreach (string address in opts.Hosts)
                {
                    addresses.Add(address);
                    OutputHelper.WriteLine($"added host '{address}'", 1);

                }

            }
            else
            {
                OutputHelper.WriteLine("3. No manual networks or addresses provided, skipping...");
            }

            manifest.Targets.AddRange(networks.Select(n => n.ToString()));
            manifest.Targets.AddRange(addresses);

            if (addresses.Count == 0 && networks.Count == 0)
                return NothingFound(opts, manifest, "After filtering - there are no networks or hosts to scan...");

            OutputHelper.WriteLine("4. Probing hosts and scanning networks for SMB port 445...");

            //TODO: add none quiet output to show what we are scanning at this point - nets, hosts and exclusiosn

            // Begin the scan for up hosts
            HostFinder
                hf = new(addresses, networks, filteredAddresses);

            OutputHelper.WriteLine($"scanning is complete and we have {hf.ReachableHosts.Count} hosts with reachable SMB services", 1);
            ProgressReporter.Current?.Counts(hosts: hf.ReachableHosts.Count);

            if (hf.ReachableHosts.Count == 0)
                return NothingFound(opts, manifest, "There are no hosts with accessible SMB services...");

            if (opts.Verbose)
            {
                OutputHelper.WriteLine($"reachable hosts:", 2);
                foreach (Host h in hf.ReachableHosts)
                    OutputHelper.WriteLine(h.Address, 3);
            }

            OutputHelper.WriteLine("5. Probing SMB services for accessible shares...");
            ProgressReporter.Current?.Stage(ProgressReporter.STAGE_SHARES);

            if (crossPlatform)
            {
                foreach (Host host in hf.ReachableHosts)
                {
                    Thread t = new(() => CrossPlatformShareFinder.DiscoverDeviceShares(host, opts.Domain, opts.Username, opts.Password));
                    t.Start();
                }
                // Wait for max scan time
                Thread.Sleep(Host.PORT_MAX_WAIT_MS * 4);
            }
            else
            {
                // Enumerate shares
                foreach (Host host in hf.ReachableHosts)
                {
                    Thread t = new(() => WindowsShareFinder.DiscoverDeviceShares(host));
                    t.Start();
                }
                // Wait for max scan time
                Thread.Sleep(Host.PORT_MAX_WAIT_MS * 4);
            }

            OutputHelper.WriteLine($"probing is complete and we have {hf.HostsWithShares.Count} hosts with accessible shares", 1);
            manifest.Hosts = hf.HostsWithShares.Count;
            ProgressReporter.Current?.Counts(hosts: hf.HostsWithShares.Count);

            if (hf.HostsWithShares.Count == 0)
                return NothingFound(opts, manifest, "There are no hosts with accessible SMB shares.  Exiting...");

            if (!opts.Quiet)
            {
                OutputHelper.WriteLine("reachable hosts with accessible SMB shares:", 2);
                foreach (Host host in hf.HostsWithShares)
                    OutputHelper.WriteLine(host.Address, 3);
            }

            // Build list of uncPaths from up hosts
            List<Share> shares = new();
            foreach (Host h in hf.HostsWithShares)
                shares.AddRange(h.Shares);

            if (opts.Verbose)
            {
                OutputHelper.WriteLine("accessible SMB shares:", 2);
                foreach (Share share in shares)
                    OutputHelper.WriteLine(share.uncPath, 3);
            }

            if (opts.ExcludeHiddenShares || opts.Shares.Any() || opts.ExcludedShares.Any())
                OutputHelper.WriteLine("6a. Filtering share list");

            if (opts.Shares.Any())
            {
                OutputHelper.WriteLine("Keeping only named shares", 1);
                shares = shares
                    .Where(item => opts.Shares.ToList().ConvertAll(i => i.ToLower()).Contains(item.Name.ToLower()))
                    .ToList();
            }

            if (opts.ExcludeHiddenShares)
            {
                OutputHelper.WriteLine("Filtering out hidden shares", 1);
                shares = shares
                    .Where(item => !item.Name.EndsWith('$'))
                    .ToList();
            }

            if (opts.ExcludedShares.Any())
            {
                OutputHelper.WriteLine("Filtering out named excluded shares", 1);
                shares = shares
                    .Where(item => !opts.ExcludedShares.ToList().ConvertAll(i => i.ToLower()).Contains(item.Name.ToLower()))
                    .ToList();
            }

            manifest.Shares = shares.Count;
            ProgressReporter.Current?.Counts(shares: shares.Count);

            if (!shares.Any())
                return NothingFound(opts, manifest, "There are no accessible SMB shares to scan.  Exiting...");

            if (opts.Verbose)
            {
                OutputHelper.WriteLine($"Shares found:", 1);
                foreach (Share s in shares)
                    OutputHelper.WriteLine(s.uncPath, 2);
            }

            OutputHelper.WriteLine("6. Enumerating accessible shares, this can be slow...");
            ProgressReporter.Current?.Stage(ProgressReporter.STAGE_FILES);

            // Find files on all the shares
            FileFinder ff = new(BuildScanOptions(opts, shares, filePatterns, crossPlatform, new List<string>()));
            manifest.Files = ff.FileCount;

            return Finish(opts, manifest);
            // TODO: know when elasticsearch sink has finished outputting
        }

        /// <summary>Options effectives de l'énumération, identiques en mode local et réseau (dont -q).</summary>
        /// <param name="localPaths">Chemins locaux déjà validés (absolus et lisibles), vide en mode réseau.</param>
        static ScanOptions BuildScanOptions(Options opts, List<Share> shares, List<string> filePatterns, bool crossPlatform, List<string> localPaths)
        {
            return new ScanOptions
            {
                Shares = shares,
                LocalPaths = localPaths,
                OutputDirectory = opts.OutputDirectory,
                FetchFiles = opts.GrabFiles,
                FilePatterns = filePatterns,
                GetPermissionsForSingleFileInDir = opts.EnumerateOnlyASingleFilesAcl,
                EnumerateAcls = !opts.DontEnumerateAcls,
                Quiet = opts.Quiet,
                Verbose = opts.Verbose,
                CrossPlatform = crossPlatform,
                IncludeFileSize = opts.SizeFile,
                IncludeAccessTime = opts.AccessTime,
                IncludeFileAttributes = opts.FileAttributes,
                IncludeFileOwner = opts.OwnerFile,
                IncludeFastHash = opts.FastHash,
                IncludeFileSignature = opts.FileSignature,
                PreserveAccessTime = opts.PreserveAccessTime,
                FileWorkers = opts.FileWorkers,
            };
        }

        /// <summary>Aide ou erreur d'arguments : 0 si l'aide/version était demandée, 2 sinon.</summary>
        static int OutputHelp<T>(ParserResult<T> result, IEnumerable<Error> errs, bool progressJson)
        {
            bool requested = errs.Any(e => e.Tag == ErrorType.HelpRequestedError || e.Tag == ErrorType.VersionRequestedError || e.Tag == ErrorType.HelpVerbRequestedError);
            HelpText helpText = HelpText.AutoBuild(result, h =>
            {
                //configure help
                h.AdditionalNewLineAfterOption = false;
                h.Heading = "";
                h.Copyright = "Apache License 2.0";
                return HelpText.DefaultParsingErrorsHandler(result, h);
            }, e => e);
            if (progressJson && !requested)
            {
                OutputHelper.UseStderrForHumanOutput();
                using ProgressReporter reporter = new(Console.Out);
                reporter.Error("arguments invalides : " + string.Join(" ; ", errs.Select(e => e.Tag.ToString())));
            }
            OutputHelper.ConsoleWriteLogo();
            OutputHelper.WriteLine(helpText);
            return requested ? ExitCodes.Ok : ExitCodes.ArgumentError;
        }

        #region Classes

        public class Options
        {

            [Option('c', "csv-file", Group = "output", Required = false, HelpText = "Output results to a CSV file by providing filepath")]
            public string CsvFile { get; set; }

            [Option('e', "elasticsearch-host", Group = "output", Required = false, HelpText = "Output results to elasticsearch by providing elasticsearch hostname (default port is 9200 , but can be overridden)")]
            public string ElasticsearchHost { get; set; }

            [Option('a', "aggression", Required = false, Default = 6, HelpText = "Vary scanning speed in a range between 1 and 10. 10 being fastest [No decimals]")]
            public int Aggression { get; set; }

            [Option("elasticsearch-port", Required = false, Default = "9200", HelpText = "Define the elasticsearch custom port if required")]
            public string ElasticsearchPort { get; set; }

            [Option('f', "fast", Required = false, HelpText = "Enumerate only one files permissions per directory")]
            public bool EnumerateOnlyASingleFilesAcl { get; set; }

            [Option('l', "scan-local-shares", Required = false, HelpText = "Scan the local shares on this machine")]
            public bool ScanLocalShares { get; set; }

            [Option("local-path", Required = false, HelpText = "Scan local directories instead of SMB network discovery (multiple accepted)")]
            public IEnumerable<string> LocalPaths { get; set; }

            [Option('D', "disable-network-discovery", Required = false, HelpText = "Disable network discovery")]
            public bool DisableNetworkDiscovery { get; set; }

            [Option('n', "network", Required = false, HelpText = "Manually add network to scan (multiple accepted)")]
            public IEnumerable<String> Networks { get; set; }
            [Option('N', "exclude-network", Required = false, HelpText = "Exclude a network from scanning (multiple accepted)")]
            public IEnumerable<string> ExcludedNetworks { get; set; }

            [Option('h', "host", Required = false, HelpText = "Manually add host to scan")]
            public IEnumerable<string> Hosts { get; set; }

            [Option('H', "exclude-host", Required = false, HelpText = "Exclude a host from scanning")]
            public IEnumerable<string> ExcludedHosts { get; set; }

            [Option('q', "quiet", Required = false, HelpText = "Disable unneccessary output")]
            public bool Quiet { get; set; }

            [Option('S', "exclude-share", Required = false, HelpText = "Do not scan shares with this name (multiple accepted)")]
            public IEnumerable<string> ExcludedShares { get; set; }

            [Option('s', "share", Required = false, HelpText = "Only scan shares with this name (multiple accepted)")]
            public IEnumerable<string> Shares { get; set; }

            [Option("file-pattern", Required = false, HelpText = "Only fetch files matching these regexes patterns")]
            public IEnumerable<string> FilePatterns { get; set; }

            [Option('g', "grab-files", Required = false, HelpText = "Grab files and store them locally")]
            public bool GrabFiles { get; set; }

            [Option("loot", Required = false, Default = "loot", HelpText = "Path to store grabbed files")]
            public string OutputDirectory { get; set; }

            [Option('E', "exclude-hidden-shares", Required = false, HelpText = "Exclude shares ending in $")]
            public bool ExcludeHiddenShares { get; set; }

            [Option('v', "verbose", Required = false, HelpText = "Give more output")]
            public bool Verbose { get; set; }

            [Option('m', "max-network-cidr-size", Required = false, Default = "20", HelpText = "Maximum network size to scan for SMB Hosts")]
            public string MaxNetworkSizeForScanning { get; set; }

            [Option('A', "dont-enumerate-acls", Required = false, Default = false, HelpText = "Skip enumeration of file ACLs")]
            public bool DontEnumerateAcls { get; set; }
            [Option('d', "domain", Required = false, Default = "", HelpText = "Domain for connecting to SMB")]
            public string Domain { get; set; }

            [Option('u', "username", Required = false, HelpText = "Username for connecting to SMB - mandatory on linux")]
            public string Username { get; set; }

            [Option('p', "password", Required = false, HelpText = "Password for connecting to SMB - mandatory on linux")]
            public string Password { get; set; }

            [Option("sizefile", Required = false, HelpText = "Collect file sizes in bytes")]
            public bool SizeFile { get; set; }

            [Option("access-time", Required = false, HelpText = "Collect last access time for files")]
            public bool AccessTime { get; set; }

            [Option("fileattributes", Required = false, HelpText = "Collect file system attributes")]
            public bool FileAttributes { get; set; }

            [Option("ownerfile", Required = false, HelpText = "Collect file owner (DOMAIN\\Username)")]
            public bool OwnerFile { get; set; }

            [Option("fasthash", Required = false, HelpText = "Compute xxHash64 for files (first 64KB)")]
            public bool FastHash { get; set; }

            [Option("file-signature", Required = false, HelpText = "Detect file type by magic bytes")]
            public bool FileSignature { get; set; }

            [Option("preserve-access-time", Required = false, HelpText = "Restore each file's last access time after reading it (--fasthash / --file-signature)")]
            public bool PreserveAccessTime { get; set; }

            [Option("file-workers", Required = false, Default = 8, HelpText = "Files examined in parallel within a directory (owner, ACL, hash, signature): 1 = one at a time, up to 64")]
            public int FileWorkers { get; set; }

            [Option("progress-json", Required = false, HelpText = "Emit one JSON progress line on stdout every ~2s and per stage (human output goes to stderr)")]
            public bool ProgressJson { get; set; }

            [Option("manifest", Required = false, HelpText = "Write a JSON manifest of the scan (options, targets, counts, columns) to this path")]
            public string ManifestPath { get; set; }

            /// <summary>
            /// Fourre-tout : tout argument positionnel restant (typiquement la fin d'un
            /// chemin non guillemeté contenant une espace) est capturé ici puis refusé,
            /// au lieu d'être jeté en silence par l'analyseur.
            /// </summary>
            [Value(0, MetaName = "extra", Hidden = true)]
            public IEnumerable<string> ExtraArgs { get; set; }

            [Usage(ApplicationAlias = "SMBeagle")]
            public static IEnumerable<Example> Examples
            {
                get
                {
                    UnParserSettings unParserSettings = new();
                    unParserSettings.PreferShortName = true;
                    yield return new Example("Output to a CSV file", unParserSettings, new Options { CsvFile = "out.csv" });
                    yield return new Example("Output to elasticsearch (Preferred)", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1" });
                    yield return new Example("Output to elasticsearch and CSV", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", CsvFile = "out.csv" });
                    yield return new Example("Disable network discovery and provide manual networks", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", DisableNetworkDiscovery = true, Networks = new List<String>() { "192.168.12.0/23", "192.168.15.0/24" } });
                    yield return new Example("Do not enumerate ACLs (FASTER)", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", DontEnumerateAcls = true });
                    yield return new Example("Collect file size metadata", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", SizeFile = true });
                    yield return new Example("Collect file access time metadata", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", AccessTime = true });
                    yield return new Example("Collect file attributes metadata", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", FileAttributes = true });
                    yield return new Example("Collect file owner metadata", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", OwnerFile = true });
                    yield return new Example("Collect fast hash metadata", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", FastHash = true });
                    yield return new Example("Collect file signature metadata", unParserSettings, new Options { ElasticsearchHost = "127.0.0.1", FileSignature = true });
                    yield return new Example("Scan local directory", unParserSettings, new Options { LocalPaths = new List<string> { "/tmp" } });
                    yield return new Example("Driven by docia (JSON progress, manifest, access times preserved)", unParserSettings, new Options { LocalPaths = new List<string> { @"D:\partage" }, CsvFile = "scan.csv", SizeFile = true, AccessTime = true, FileAttributes = true, OwnerFile = true, FastHash = true, FileSignature = true, PreserveAccessTime = true, ProgressJson = true, ManifestPath = "scan.json" });
                }
            }
        }

        #endregion
    }
}
