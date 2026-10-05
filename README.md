<p align="center">
    <img src="Server/icon_media/walkuo-512.png" width="256">
</p>

# WalkUO

WalkUO is a fork of RunUO, an Ultima Online server emulator, focused on long-term support (LTS) and gradual evolution.

## Build

Inside the WalkUO folder, run the following command:

#### Windows

```console
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc /optimize /unsafe /t:exe /out:WalkUO.exe /win32icon:Server\walkuo.ico /d:NEWTIMERS /d:NEWPARENT /recurse:Server\\*.cs
```

#### Linux (MONO)

```console
TERM=dumb mcs -langversion:5 -optimize+ -unsafe -t:exe -out:WalkUO.exe -win32icon:Server/walkuo.ico -nowarn:219,414 -d:NEWTIMERS -d:NEWPARENT -d:MONO -reference:System.Drawing -recurse:'Server/*.cs'
```

To run the server:

```console
TERM=dumb mono WalkUO.exe
```

> [!note]
>
> `TERM=dumb` selects a minimal terminal definition to avoid Mono console initialization failures (`File must be smaller than 4K`) with larger terminfo entries. Use it for both compilation and execution.

## Client

I recommend using [ClassicUO](https://github.com/ClassicUO/ClassicUO) to connect to the server. It's a modern, open-source implementation that offers a number of quality-of-life improvements, such as adjustable resolution, among others.

## Modern Alternatives to RunUO/WalkUO

If you are looking for a modern emulator, in tune with the latest expansions and most recent updates of *Ultima Online*, and supported by an active and vibrant community, give **ServUO** a try.

* Community: [https://www.servuo.dev](https://www.servuo.dev)
* Source code: [https://github.com/ServUO/ServUO](https://github.com/ServUO/ServUO)

If you are looking for an even more modern and highly promising emulator, still in development and not yet production-ready, give **ModernUO** a try.

* Website: [https://modernuo.com](https://modernuo.com)
* Community (Discord): [https://muo.gg/discord](https://muo.gg/discord)
* Source code: [https://github.com/modernuo/ModernUO](https://github.com/modernuo/ModernUO)
