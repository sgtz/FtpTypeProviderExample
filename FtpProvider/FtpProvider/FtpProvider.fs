module FtpTypeProviderImplementation

open System
open System.Net
open System.IO
open System.Reflection
open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices

[<Literal>]
let diag=true  // TODO.4  TODO.5

/// Get the directories and files in an FTP site using anonymous login
let getFtpDirectory (site:string, user:string, pwd:string) = 
    let request = 
        match WebRequest.Create(site) with 
        | :? FtpWebRequest as f -> f
        | _ -> failwith (sprintf "site '%s' did not result in an FTP request. Do you need to add prefix 'ftp://' ?" site)
    request.Method <- WebRequestMethods.Ftp.ListDirectoryDetails
    request.Credentials <- NetworkCredential(user, pwd)

    use response = request.GetResponse() :?> FtpWebResponse
    
    use responseStream = response.GetResponseStream()
    use reader = new StreamReader(responseStream)
    let contents = 
        [ while not reader.EndOfStream do 
             yield reader.ReadLine().Split([| ' ';'\t' |],StringSplitOptions.RemoveEmptyEntries) ]

    let dirs = 
        [ for c in contents do 
            if c.Length > 1 then 
               if c.[0].StartsWith("d") then yield Seq.last c ]

    let files = 
        [ for c in contents do 
            if c.Length > 1 then 
               if c.[0].StartsWith("-") then yield Seq.last c ]

    files, dirs

// getFtpDirectory  "ftp://ftp.ncbi.nlm.nih.gov/"

[<TypeProvider>]
type FtpProviderImpl(config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces ()
    let nameSpace = "FSharp.Management"
    let asm = Assembly.GetExecutingAssembly()

    // Recursive, on-demand adding of types
    let createTypes (typeName, site, user, pwd:string) = 
        let rec addTypes (site:string, td:ProvidedTypeDefinition) =

            td.AddMembersDelayed(fun () -> 
                // TODO.6
                if diag then printfn "debug: getting site: %s, user: %s, pwd: %s" site user (String.replicate (pwd.Length) "*")
                let files, dirs = getFtpDirectory (site, user, pwd)

                [
                    if diag then printfn "- debug: list dirs and files" 
                    for dir in dirs do 
                        let nestedType = ProvidedTypeDefinition(dir, Some typeof<obj>)
                        addTypes(site + dir + "/", nestedType)
                        yield nestedType :> MemberInfo 

                    for file in files do 

                        // BUG.1

                        let nestedType = ProvidedTypeDefinition(file, Some typeof<obj>)

                        let contentsProperty =                                   
                                ProvidedProperty("Contents", typeof<obj>,
                                                    
                                                   IsStatic=true,

//                                                     GetterCode = (fun args -> <@@ "the file contents" @@> ))

                                                   GetterCode = 
                                                        (fun args -> 
                                                            <@@ 
                                                                if diag then printfn "debug: getting %s%s" site file
                                                                let request = WebRequest.Create(site + file) :?> FtpWebRequest
                                                    
                                                                if diag then printf "  - debug: 1)getting"
                                                                request.Method <- WebRequestMethods.Ftp.DownloadFile
                                                                request.Credentials <- new NetworkCredential(user, pwd) 
                                                                let response = request.GetResponse() :?> FtpWebResponse   // TODO.2 
                                                    
                                                                if diag then printf ",2)streaming"
                                                                use responseStream = response.GetResponseStream()
                                                                use reader = new StreamReader(responseStream)
                                                    
                                                                if diag then printf ",3)reading"
                                                                let r = reader.ReadToEnd() :> obj
                                                                if diag then printfn ",4)returning info"
                                                                r
                                                                // TODO.
                                                                // TODO.3

                                                              @@>))
                        nestedType.AddMember contentsProperty

                        yield nestedType :> MemberInfo 
                   ]
                   )
        let actualType = ProvidedTypeDefinition(asm, nameSpace, typeName, Some typeof<obj>)
        addTypes(site, actualType)
        actualType

    let _ = 
        let topType = ProvidedTypeDefinition(asm, nameSpace, "FtpProvider", Some typeof<obj>)
        let siteParam = 
           let p = ProvidedStaticParameter("Url",typeof<string>) 
           p.AddXmlDoc(sprintf "The URL of the FTP site, including ftp://")
           p
        let userParam = 
           let p = ProvidedStaticParameter("User",typeof<string>, "anonymous") 
           p.AddXmlDoc("The user of the FTP site (default 'anonymous')")
           p
        let pwdParam = 
           let p = ProvidedStaticParameter("Password",typeof<string>, "janedoe@contoso.com") 
           p.AddXmlDoc("The password used to access the FTP site (default 'janedoe@contoso.com')")
           p
        let staticParams = [ siteParam; userParam; pwdParam ]
        topType.DefineStaticParameters(staticParams, (fun typeName args -> 
            let site = args.[0] :?> string
            let user = args.[1] :?> string  |> fun s -> if String.IsNullOrEmpty(s) then "anonymous" else s           // TODO.1
            let pwd =  args.[2] :?> string  |> fun s -> if String.IsNullOrEmpty(s) then "janeDoe@contoso.com" else s
            createTypes(typeName, site, user, pwd)))
        this.AddNamespace(nameSpace, [topType])

[<assembly:TypeProviderAssembly>]
do ()

// TODO
// ----

// TODO.1: pick up FTP courtesy details from environment variables?
// TODO.2: add progress updates via intellisense?  ie. |          |  and |****  underneath for a text based left to right gauge
// TODO.3: return `notfound or exception on error
// TODO.4: consider having a verbose and diagnostic modes to std out?  ie. consider actually keeping them after testing is complete
// TODO.5: allow this to be set through type provider instantiation param <>
// TODO.6: add diag info to intellisense to cover command line + also IDE support (ie. so that it's transparent irrespective of usage style)
// TODO.7: get back files in various formats such as text and binary.  Include means to be able to flick on / off all the important FTP switches
// TODO.8: include ways to get file sizes

// BUG
// ---

// DONE
// ----

// FIXED
// -----
// BUG.1: file identifiers are never defined.  Is this because of an error in the quotation?  Why?

// NOTES
// -----
// NB. possibly the original example Don translated to F# live?  https://msdn.microsoft.com/en-us/library/ms229711%28v=vs.110%29.aspx?f=255&MSPPError=-2147217396
