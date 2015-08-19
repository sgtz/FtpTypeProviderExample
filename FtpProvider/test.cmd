@fsc will compile and run test.fsx (ie. emulate fsi / VS Intellisense "interpreting" the dotted-into sub phrases)
@This way there are no side effects for the VS IDE.  It's a cleaner way to test, because the process will die after being run and release any .dll file locks it may have so that you can compile again afterwards.
fsc.exe test.fsx
