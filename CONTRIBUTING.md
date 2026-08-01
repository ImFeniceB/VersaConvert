# Contribuire a VersaConvert

Grazie per voler migliorare il progetto.

## Ambiente

1. Installa .NET SDK 8 su Windows.
2. Clona il repository.
3. Esegui `dotnet test VersaConvert.sln -c Release`.
4. Avvia l’app con `dotnet run --project src/VersaConvert.App`.

FFmpeg nel `PATH` è sufficiente per lo sviluppo. In alternativa esegui `scripts/fetch-ffmpeg.ps1` per copiarlo in `vendor/`.

## Pull request

- mantieni separata la logica da WPF quando possibile;
- aggiungi test per nuovi formati o strategie di output;
- non costruire comandi concatenando input dell’utente: usa `ProcessStartInfo.ArgumentList`;
- aggiorna `docs/FORMATI.md` se cambia la matrice;
- verifica una conversione reale per ogni nuovo motore.

## Segnalazioni

Per un bug indica formato sorgente, formato di uscita, versione Windows e messaggio mostrato. Non allegare file privati o protetti da copyright se non puoi condividerli.
