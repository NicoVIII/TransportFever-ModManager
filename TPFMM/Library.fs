﻿namespace TPFModManager

open FSharp.Data
open System
open System.IO
open System.IO.Compression
open System.Net
open System.Text.RegularExpressions

type private Url = Url of String
type private WebCode = WebCode of String

type Mods = JsonProvider<""" { "installed_mods": [{ "name": "s", "url": "s", "websiteVersion": "s" }, { "name": "s", "url": "s", "websiteVersion": "s" } ] } """>


// Internal logic to provide API functionality
module private Internal =
    // Setting Management
    type Settings = JsonProvider<""" { "tpfModPath": "/path/to/tpf", "deleteZips": true } """>

    let settingsFileExists settingsPath =
        File.Exists settingsPath

    let tryLoadSettings () =
        let settingsPath = "settings.json"
        match settingsFileExists settingsPath with
        | true -> Some (Settings.Load settingsPath)
        | false -> None

    type InstallStatus = | Installed | NotInstalled

    let safeBytes (file :string) bytes =
        let directory = file.Split [| '/' |] |> Array.toList |> List.rev |> List.tail |> List.rev |> List.fold (fun path folder -> path+folder+"/") ""
        Directory.CreateDirectory directory |> ignore
        File.WriteAllBytes(file, bytes)

    let safeString (file :string) str =
        let directory = file.Split [| '/' |] |> Array.toList |> List.rev |> List.tail |> List.rev |> List.fold (fun path folder -> path+folder+"/") ""
        if directory.Length = 0 then () else Directory.CreateDirectory directory |> ignore
        File.WriteAllText(file, str)

    let loadModInfoFrom (path :string) =
        let mods = Mods.Load(path)
        mods.InstalledMods |> Array.toList

    let loadModInfo() = loadModInfoFrom "mods.json"

    let safeModInfoTo (mods :Mods.InstalledMod list) (path :string) =
        let modsObj = new Mods.Root(Array.ofList mods)
        safeString path (modsObj.ToString())

    let safeModInfo mods = safeModInfoTo mods "mods.json"

    let modStatus (Url url) =
        let mods = loadModInfo()
        let fold state (m :Mods.InstalledMod) = state || m.Url = url
        let installed = List.fold fold false mods
        match installed with
        | true -> Installed 
        | false -> NotInstalled

    // Functionality
    let cookieContainer = new CookieContainer()

    let acceptTerms (site :HtmlDocument) (Url urlString) =
        let fold inputs input =
            let fold (n, v) (atr :HtmlAttribute) =
                match atr.Name() with
                | "name" -> (atr.Value(), v)
                | "value" -> (n, atr.Value())
                | _ -> (n, v)
            let atrPair = HtmlNode.attributes input |> List.fold fold ("", "")
            atrPair::inputs
        
        let header = site.CssSelect("header h1").ToString()
        if header.Contains "Terms" || header.Contains "Nutzungsbedingungen" then
            let query = site.CssSelect("#content form input") |> List.fold fold []
            let action = site.CssSelect("#content form")
            match action with
            | [action] ->
                let action' = action |> HtmlNode.attribute "action"
                // Send HttpRequest
                Http.RequestString (action'.Value(), body = FormValues query, cookieContainer=cookieContainer) |> ignore
                Http.RequestString (urlString, cookieContainer=cookieContainer) |> HtmlDocument.Parse
            | _ -> failwith ("[Error] Confirmation of Terms failed! - "+urlString)
        else
            site

    let tryGetSite url =
        let (Url urlString) = url
        try
            let site = Http.RequestString (urlString, cookieContainer=cookieContainer) |> HtmlDocument.Parse
            Some (acceptTerms site url)
        with
        | :? System.Net.WebException as ex -> None

    let nameFromSite (source :HtmlDocument) (Url urlString) =
        let node = source.CssSelect("#content header > h1 > a")
        match node with
        | [header] -> HtmlNode.innerText header
        | _ -> failwith "[Error] Unsupported layout of website! (name)"+urlString

    let versionFromSite (source :HtmlDocument) (Url urlString) =
        let node = source.CssSelect(".messageBody")
        match node with
        | [node] ->
            let text = node.ToString()
            let m = Regex.Match(text, @"<dt>[\sA-z0-9]*?[Vv]ersion[\sA-z0-9]*?</dt>[\s\r\n]*<dd>[\s\r\n]*(.*?)[\s\r\n]*</dd>")
            match m.Success with
            | true ->
                Some m.Groups.[1].Value
            | false -> None
        | _ -> None

    let filePathFromSite (source :HtmlDocument) (Url urlString) =
        let node = source.CssSelect(".filebaseFileList h3 > a")
        match node with
        | [node] ->
            let atr = node.Attribute "href"
            Some (atr.Value ())
        | _ ->
            printfn "[Error] Mods with more than one downloadable file are not supported yet. %s" urlString
            None
    
    let downloadMod (_mod :Mods.InstalledMod) fileUrl target =
            printfn "%s - %s:" _mod.Name _mod.WebsiteVersion
            printf "* Downloading..."
            match Http.Request(fileUrl, cookieContainer=cookieContainer).Body with
            | Text text ->
                failwith "Invalid filepath!"
            | Binary bytes -> 
                safeBytes target bytes
            printfn "\r%-16s" "* Downloaded."

    let extractMod tpfModPath zipPath =
        try
            ZipFile.ExtractToDirectory(zipPath, tpfModPath) |> ignore
        with
        | :? System.IO.IOException -> ()

    let installMod _mod tpfPath zipPath =
        printf "* Installing..." |> ignore
        extractMod tpfPath zipPath
        safeModInfo (_mod::loadModInfo())
        printfn "\r%-15s" "* Installed." |> ignore

    let downloadAndInstall (settings :Settings.Root) url =
        let (Url urlString) = url
        match modStatus url with
        | Installed ->
            printfn "[Info] A mod with url '%s' is already installed." urlString
        | NotInstalled ->
            let (Url urlString) = url
            let source = tryGetSite url
            match source with
            | Some source ->
                let name = nameFromSite source url
                let version = versionFromSite source url
                let filePath = filePathFromSite source url
                match (version, filePath) with
                | (Some version, Some filePath) ->
                    let _mod = new Mods.InstalledMod(name, urlString, version)
                    let zipPath = "tmp/"+_mod.Name+"-"+_mod.WebsiteVersion+".zip"
                    downloadMod _mod filePath zipPath
                    installMod _mod settings.TpfModPath zipPath
                | _ -> ()
            | None -> ()
        printfn ""

    let downloadAndInstallAll urls =
        let settings = tryLoadSettings ()
        match settings with
        | None -> invalidOp "Please set a modPath"
        | (Some settings) when settings.TpfModPath = "" -> invalidOp "Please set a modPath"
        | Some settings ->
            urls |> List.iter (fun url -> downloadAndInstall settings url)
            if settings.DeleteZips && Directory.Exists("tmp") then Directory.Delete("tmp", true)

    let update () =
        let fold list (_mod :Mods.InstalledMod) =
            let site = tryGetSite (Url _mod.Url)
            match site with
            | Some site ->
                let newVersion = versionFromSite site (Url _mod.Url)
                if not (newVersion = Some _mod.WebsiteVersion) then
                    match newVersion with
                    | Some newVersion -> (_mod.Name, _mod.WebsiteVersion, newVersion)::list
                    | None -> list
                else
                    list
            | None -> list

        loadModInfo()
        |> List.fold fold []
        |> List.map (fun (name, oldVersion, newVersion) -> [| name ; oldVersion ; newVersion |])
        |> Array.ofList

    let list () =
        loadModInfo()
        |> List.sortBy (fun m -> m.Name) 
        |> Array.ofList

// API
type TPFMM =
    static member List = Internal.list ()
    static member Install urls =
        Array.toList urls
        |> List.map (fun url -> Url url)
        |> Internal.downloadAndInstallAll
    static member Update = Internal.update ()