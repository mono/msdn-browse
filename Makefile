all : msdnb.exe

msdnb.exe : *.cs
	mcs *.cs /out:$@ -pkg:gtk-sharp-2.0,gecko-sharp-2.0

r : msdnb.exe
	LD_LIBRARY_PATH=$(LD_LIBRARY_PATH):/usr/lib/mozilla-1.7.8/ mono msdnb.exe
