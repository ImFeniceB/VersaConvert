# Componenti di terze parti

## FFmpeg

La build completa di VersaConvert incorpora `ffmpeg.exe` come programma separato, invocato mediante processo locale.

- progetto: https://ffmpeg.org/
- codice sorgente: https://ffmpeg.org/download.html#get-sources
- build Windows usata dallo script: https://www.gyan.dev/ffmpeg/builds/
- licenza: la build “full” o “essentials” può includere componenti GPL; consultare l’output `ffmpeg -L` e https://ffmpeg.org/legal.html

FFmpeg non è coperto dalla licenza MIT di VersaConvert. I titolari dei diritti di FFmpeg e delle librerie collegate mantengono tutti i rispettivi diritti.

## .NET

VersaConvert è compilato con .NET 8 e, nella pubblicazione self-contained, include componenti del runtime .NET distribuiti da Microsoft con licenza MIT. Informazioni: https://github.com/dotnet/runtime
