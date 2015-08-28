module FtpTypeProviderImplementation

open System
open System.Net
open System.IO
open System.Reflection
open ProviderImplementation.ProvidedTypes
open Microsoft.FSharp.Core.CompilerServices

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

[<TypeProvider>]
type FtpProviderImpl(config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces ()
    let nameSpace = "FSharp.Management"
    let asm = Assembly.GetExecutingAssembly()

    // Recursive, on-demand adding of types
    let createTypes (typeName, site, user, pwd:string, useBinary:bool) = 
        let rec addTypes (site:string, td:ProvidedTypeDefinition) =

            td.AddMembersDelayed(fun () -> 
                // TODO.6
                let files, dirs = getFtpDirectory (site, user, pwd)
                [
                    for dir in dirs do 
                        let nestedType = ProvidedTypeDefinition(dir, Some typeof<obj>)
                        addTypes(site + dir + "/", nestedType)
                        yield nestedType :> MemberInfo 

                    for file in files do 

                        let nestedType = ProvidedTypeDefinition(file, Some typeof<obj>)

                        let contentsProperty =                                   
                                ProvidedProperty("Contents", typeof<obj>,
                                                   IsStatic=true,
                                                   GetterCode = 
                                                        (fun args -> 
                                                            <@@ 
                                                                let request = WebRequest.Create(site + file) :?> FtpWebRequest
                                                    
                                                                request.Method <- WebRequestMethods.Ftp.DownloadFile
                                                                request.UseBinary <- useBinary
                                                                request.Credentials <- new NetworkCredential(user, pwd) 
                                                                let response = request.GetResponse() :?> FtpWebResponse   // TODO.2 
                                                    
                                                                use responseStream = response.GetResponseStream()
                                                                use reader = new StreamReader(responseStream)
                                                    
                                                                let r = reader.ReadToEnd() :> obj
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

    let addProvidedStaticParameter nme typ xmldoc = 
      let p = ProvidedStaticParameter(nme,typ) 
      p.AddXmlDoc(sprintf xmldoc)
      p

    let _ = 
        let a = ProvidedTypeDefinition(asm, nameSpace, "FtpProvider", Some typeof<obj>)
        a.AddXmlDoc("An FTP Type Provider which lets you 'dot' into directory structures, and then retrieve a file by 'dotting' into the '.Contents' tag.  note: there are no progress updates, so if it's a large file over a slow connection, the only solution is to wait.  Perhaps try a smaller file first to verify.")  // BUG.2 

        let topType = ProvidedTypeDefinition(asm, nameSpace, "FtpProvider", Some typeof<obj>)
        let siteParam = 
           let p = ProvidedStaticParameter("Url",typeof<string>,"") 
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
        let useBinary = 
            let p = ProvidedStaticParameter("UseBinary",typeof<bool>, true)
            p.AddXmlDoc("sets the data transfer data type to be binary (true) or ascii (false).  Binary mode gives a true, exact representation.  More often than not is the safer thing to use as this mode will handle both text and binary.  Use Ascii mode if you want to transfer text only, and you are happy to let FTP decide on appropriate line break characters translations, etc.")
            p
        let staticParams = [ siteParam; userParam; pwdParam; useBinary ]
        topType.DefineStaticParameters(staticParams, (fun typeName args -> 
            let site = args.[0] :?> string
            let user = args.[1] :?> string
            let pwd =  args.[2] :?> string
            let useBinary = args.[3] :?> bool
            createTypes(typeName, site, user, pwd, useBinary)))  // pass in top type details
        this.AddNamespace(nameSpace, [topType])

[<assembly:TypeProviderAssembly>]
do ()

// TODO
// ----

// TODO.1: pick up FTP courtesy details from environment variables?
// TODO.2: add progress updates via intellisense?  ie. |          |  and |****  underneath for a text based left to right gauge
// TODO.3: return `notfound or exception on error
// TODO.6: add diag info to intellisense to cover command line + also IDE support (ie. so that it's transparent irrespective of usage style)
// TODO.8: include ways to get file sizes

// BUG
// ---
// BUG.2: this text is not showing up in intellisense

// DONE
// ----
// TODO.7: get back files in various formats such as text and binary.  Include means to be able to flick on / off all the important FTP switches

// FIXED
// -----
// BUG.1: file identifiers are never defined.  Is this because of an error in the quotation?  Why?

// RESOLVED
// --------
// TODO.4: consider having a verbose and diagnostic modes to std out?  ie. consider actually keeping them after testing is complete
//  -- decided not to
// TODO.5: allow this to be set through type provider instantiation param <>
//  -- decided not to

// NOTES
// -----
// NB. possibly the original example Don translated to F# live?  https://msdn.microsoft.com/en-us/library/ms229711%28v=vs.110%29.aspx?f=255&MSPPError=-2147217396
