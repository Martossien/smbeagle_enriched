namespace SMBeagle
{
    /// <summary>Codes de retour du processus (documentés dans le README).</summary>
    static class ExitCodes
    {
        /// <summary>Scan terminé, fichiers écrits.</summary>
        public const int Ok = 0;
        /// <summary>Erreur d'exécution (E/S, exception).</summary>
        public const int RuntimeError = 1;
        /// <summary>Arguments invalides ou incohérents.</summary>
        public const int ArgumentError = 2;
        /// <summary>Aucune cible à scanner ou rien trouvé (aucun hôte, partage ou fichier).</summary>
        public const int NothingFound = 3;
        /// <summary>
        /// Scan terminé et CSV écrit, mais **une cible demandée n'a pas été scannée**
        /// (accès refusé, montage cassé). Les fichiers écrits sont bons ; le périmètre
        /// est incomplet.
        ///
        /// Ce code existe parce qu'un `0` mentait : un partage entier pouvait sortir de
        /// l'audit sur une ligne d'avertissement noyée dans la sortie, et rien, ni dans
        /// le CSV ni en aval, ne disait qu'il manquait. Un audit sert à décider de
        /// suppressions : « je n'ai pas tout vu » doit être un fait, pas une trace.
        /// Le détail est dans le manifeste (`skipped`).
        /// </summary>
        public const int PartialScan = 4;
    }
}
