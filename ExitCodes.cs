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
    }
}
