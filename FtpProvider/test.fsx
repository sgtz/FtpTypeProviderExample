#r @"FtpProvider\bin\Debug\FtpTypeProvider.dll"

type F = FSharp.Management.FtpProvider<"ftp://ftp.ncbi.nlm.nih.gov/">

let file  = F.genomes.Drosophila_melanogaster.``RELEASE_4.1``.CHR_2.``NT_033778.asn``.GetContents 
file :?> string |> (fun s->s.Substring(0,500))

//let file2 = F.genomes.Drosophila_melanogaster.``RELEASE_4.1``.CHR_2.``NT_033778.asn``.GetContentsAsync 


// let x = 1
