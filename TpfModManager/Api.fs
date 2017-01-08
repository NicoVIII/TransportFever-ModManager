﻿namespace TpfModManager

[<AllowNullLiteral>]
type Settings(``internal``: SettingsModule.T) =
    let mutable ``internal`` = ``internal``

    member x.Save () = SettingsModule.saveSettings ``internal``
    member x.TpfModPath
        with get() =
            ``internal``.tpfModPath
        and set(value) = 
            ``internal`` <- {``internal`` with tpfModPath = value}
    new () = Settings({tpfModPath = ""})

module private SettingsApi =
    let convert settings =
        match settings with
        | None -> null
        | Some settings -> new Settings(settings)

type Version(``internal``: ModList.Version) =
    member val Major =
        ``internal``.major
        with get, set
    member val Minor =
        ``internal``.minor
        with get, set

type Mod(``internal``: ModList.Mod) =
    member val Image =
        match ``internal``.image with
        | None -> ""
        | Some i -> i
        with get, set
    member val Folder =
        ``internal``.folder
        with get, set
    member val Name =
        ``internal``.name
        with get, set
    member val Version =
        new Version(``internal``.version)
        with get, set

module private ModApi =
    let convert ``mod`` =
        new Mod(``mod``)

type ModManager() =
    member val Settings =
        SettingsModule.loadSettings()
        |> SettingsApi.convert
        with get, set
    member val ModList =
        ModList.loadModList()
        |> List.map ModApi.convert
        |> List.toArray
        with get, set

    member x.Check() =
        x.ModList <-
            ModList.createModListFromPath x.Settings.TpfModPath
            |> List.map ModApi.convert
            |> List.toArray