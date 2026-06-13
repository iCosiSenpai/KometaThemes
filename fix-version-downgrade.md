# Aggiornamento versione interna del Plugin KometaThemes a 0.9.2.0

## Obiettivo
Risolvere il problema di downgrade automatico su Jellyfin. L'utente ha installato l'aggiornamento 0.9.2.0, ma al riavvio del server la versione risultava essere la 0.9.0.0. Questo è causato dalla mancata modifica della versione dell'assembly all'interno del file `Directory.Build.props`, che fa in modo che la DLL compilata mantenga la vecchia versione.

## Passaggi di implementazione

1.  **Aggiornare Directory.Build.props**:
    *   Modificare il file `Directory.Build.props` e impostare le proprietà `<Version>`, `<AssemblyVersion>` e `<FileVersion>` a `0.9.2.0`.
2.  **Compilare e Pacchettizzare**:
    *   Eseguire la build del progetto per generare una nuova DLL con i metadati corretti.
    *   Creare un nuovo archivio `KometaThemes.zip` contenente la nuova DLL.
3.  **Aggiornare le Release e i Manifest**:
    *   Calcolare il nuovo hash MD5 del file `.zip`.
    *   Aggiornare il checksum in `manifest.json` e `repository.json` nel repository principale.
    *   Aggiornare il checksum nel `manifest.json` del repository `iCosiSenpai-Plugins`.
    *   Effettuare il commit e il push delle modifiche per entrambi i repository.
    *   Aggiornare la release `v0.9.2.0` su GitHub sostituendo l'asset `KometaThemes.zip` con quello nuovo.

## Verifica

*   L'hash MD5 nei file `.json` corrisponde al nuovo file `.zip`.
*   L'asset `KometaThemes.zip` nella release `v0.9.2.0` su GitHub è stato sostituito.
*   Le modifiche ai repository sono state caricate (push) correttamente.