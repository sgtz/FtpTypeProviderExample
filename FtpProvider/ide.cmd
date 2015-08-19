@REM it's better to load .fsx files in a different process, because when you are in a compile->test->compile loop, it's easier to kill off any references
@REM the VS IDE might make on your behalf just by having the .fsx file open.  Saves you getting caught out asking intermittantly, 'why isn't it copying after compiling okay?'
devenv test.fsx
