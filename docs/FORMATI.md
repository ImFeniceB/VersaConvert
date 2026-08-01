# Matrice dei formati

VersaConvert raggruppa i file in famiglie semantiche. Quando sono selezionati più file, l’interfaccia mostra l’intersezione delle uscite compatibili; in questo modo una coda mista di MP4 e WAV, per esempio, propone soltanto formati audio.

## Video

Ingressi: `mp4`, `mkv`, `mov`, `avi`, `webm`, `wmv`, `flv`, `m4v`, `mpeg`, `mpg`, `3gp`, `ts`, `mts`, `m2ts`.

| Uscita | Codifica predefinita | Note |
|---|---|---|
| MP4 | H.264 + AAC | compatibilità generale, fast-start web |
| MKV | H.264 + AAC | contenitore flessibile |
| WebM | VP9 + Opus | adatto al web, più lento |
| MOV | H.264 + AAC | flussi Apple e creativi |
| AVI | MPEG-4 + MP3 | compatibilità legacy |
| GIF | 15 fps, larghezza massima 1280 px | audio rimosso |
| MP3 | LAME | estrae soltanto l’audio |
| WAV | PCM 16 bit | non compresso |
| FLAC | FLAC livello 8 | lossless |
| M4A | AAC | audio compatto |
| OGG | Vorbis | formato aperto |
| Opus | Opus | efficiente per voce e musica |

## Audio

Ingressi: `mp3`, `wav`, `flac`, `aac`, `m4a`, `ogg`, `opus`, `wma`, `aiff`, `aif`, `alac`, `ac3`.

Uscite: `mp3`, `wav`, `flac`, `m4a`, `aac`, `ogg`, `opus`.

Il bitrate selezionato si applica ai formati lossy; WAV e FLAC ignorano correttamente l’opzione.

## Immagini

Ingressi: `png`, `jpg`, `jpeg`, `webp`, `bmp`, `gif`, `tiff`, `tif`, `ico`, `avif`, `heic`.

Uscite: `png`, `jpg`, `webp`, `avif`, `bmp`, `tiff`, `gif`.

Per immagini animate convertite verso un formato statico viene usato il primo fotogramma. La disponibilità di alcuni codec moderni, soprattutto HEIC in ingresso e AVIF in uscita, dipende dalle funzionalità della build FFmpeg incorporata.

## Testo

Ingressi e uscite: `txt`, `md`, `html`.

Il convertitore interno conserva UTF-8, trasforma titoli, enfasi e link Markdown di base e rimuove in sicurezza i tag durante l’esportazione a testo semplice. Non è pensato come sostituto di un motore editoriale completo.

## Documenti Office

Questi formati richiedono LibreOffice installato sul computer.

| Famiglia | Ingressi | Uscite |
|---|---|---|
| Documenti | DOC, DOCX, ODT, RTF | PDF, DOCX, ODT, RTF, TXT, HTML |
| Fogli | XLS, XLSX, ODS, CSV | PDF, XLSX, ODS, CSV |
| Presentazioni | PPT, PPTX, ODP | PDF, PPTX, ODP |

Le conversioni Office sono eseguite in modalità headless. L’aspetto finale può variare se nel sistema mancano i font usati dal documento originale.

## Non supportato nella versione 1.0

- archivi ZIP/7Z/RAR/TAR;
- modelli 3D e file CAD;
- ebook EPUB/MOBI;
- conversione PDF in immagini o documenti modificabili;
- riconoscimento OCR;
- file protetti da password o DRM.

Queste famiglie possono essere aggiunte tramite nuovi motori senza modificare l’interfaccia principale; vedi [ROADMAP.md](ROADMAP.md).
