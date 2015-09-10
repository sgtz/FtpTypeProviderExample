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

open System
open System.Threading
open System.Threading.Tasks

//This extends the Async module to add the
//AwaitTaskVoid function, which will now appear
//in intellisense
module Async =
    let inline awaitPlainTask (task: Task) =
        // rethrow exception from preceding task if it fauled
        let continuation (t : Task) : unit =
            match t.IsFaulted with
            | true -> raise t.Exception
            | arg -> ()
        task.ContinueWith continuation |> Async.AwaitTask

    let inline startAsPlainTask (work : Async<unit>) =
      Task.Factory.StartNew(fun () -> work |> Async.RunSynchronously)

    let AwaitVoidTask : (Task -> Async<unit>) =
      Async.AwaitIAsyncResult >> Async.Ignore

[<TypeProvider>]
type FtpProviderImpl(config : TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces ()
    let nameSpace = "FSharp.Management"
    let asm = Assembly.GetExecutingAssembly()

    // Recursive, on-demand adding of types
    let createTypes (typeName, site, useBinary:bool, user, pwd:string) = 
        let rec addTypes (site:string, td:ProvidedTypeDefinition) =
            td.AddMembersDelayed(fun () -> 
                let files, dirs = getFtpDirectory (site, user, pwd)
                [
                    for dir in dirs do 
                        let nestedType = ProvidedTypeDefinition(dir, Some typeof<obj>)
                        addTypes(site + dir + "/", nestedType)
                        yield nestedType :> MemberInfo 

                    for file in files do 

                        let nestedType = ProvidedTypeDefinition(file, Some typeof<obj>)

                        let getterQuotation = 
                                      (fun args -> 
                                          <@@ 
                                              let request = WebRequest.Create(site + file) :?> FtpWebRequest
                                                    
                                              request.Method <- WebRequestMethods.Ftp.DownloadFile
                                              request.UseBinary <- useBinary
                                              request.Credentials <- new NetworkCredential(user, pwd) 
                                              let response = request.GetResponse() :?> FtpWebResponse   
                                                    
                                              use responseStream = response.GetResponseStream()
                                              if useBinary then
                                                  use ms = new MemoryStream()
                                                  responseStream.CopyTo(ms)
                                                  ms.ToArray() :> obj
                                              else
                                                  use reader = new StreamReader(responseStream)
                                                  reader.ReadToEnd() :> obj
                                            @@>)

                        let contentsProperty =                                   
                                ProvidedProperty("GetContents", typeof<obj>,
                                                   IsStatic=true,
                                                   GetterCode = getterQuotation)
                        nestedType.AddMember contentsProperty

                        let getterQuotationAsync = 
                              (fun args -> 
                                          <@@ 
                                              async {

                                                  let request = WebRequest.Create(site + file) :?> FtpWebRequest
                                                    
                                                  request.Method <- WebRequestMethods.Ftp.DownloadFile
                                                  request.UseBinary <- useBinary
                                                  request.Credentials <- new NetworkCredential(user, pwd) 
                                                  let response = request.GetResponse() :?> FtpWebResponse   
                                                    
                                                  use responseStream = response.GetResponseStream() 
                                                  if useBinary then
                                                      use ms = new MemoryStream()
                                                      do! responseStream.CopyToAsync(ms) |> Async.AwaitVoidTask
                                                      return ms.ToArray() :> obj
                                                  else
                                                      use reader = new StreamReader(responseStream)
                                                      let! r = reader.ReadToEndAsync() |> Async.AwaitTask
                                                      return r :> obj
                                              }
                                            @@>)

                        let contentsPropertyAsync =                                   
                                ProvidedProperty("GetContentsAsync", typeof<Async<obj>>,
                                                   IsStatic=true,
                                                   GetterCode = getterQuotationAsync)
                        nestedType.AddMember contentsPropertyAsync

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
            let p = ProvidedStaticParameter("UseBinary",typeof<bool>, false)
            p.AddXmlDoc("sets the data transfer data type to be binary (true) or the default of ascii (false).  Binary mode gives a true, exact representation.  More often than not is the safer thing to use as this mode will handle both text and binary.  Use Ascii mode if you want to transfer text only, and you are happy to let FTP decide on appropriate line break characters translations, etc.")
            p
        let staticParams = [ siteParam; useBinary; userParam; pwdParam ]
        topType.DefineStaticParameters(staticParams, (fun typeName args -> 
            let site = args.[0] :?> string
            let useBinary = args.[1] :?> bool
            let user = args.[2] :?> string
            let pwd =  args.[3] :?> string
            createTypes(typeName, site, useBinary, user, pwd)))  // pass in top type details
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


// NOTES
// -----
// NB. possibly the original example Don translated to F# live?  https://msdn.microsoft.com/en-us/library/ms229711%28v=vs.110%29.aspx?f=255&MSPPError=-2147217396
