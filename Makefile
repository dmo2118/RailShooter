# OpenTK needs -sdk:4, at the very least for System.Threading.Monitor.Enter(object, ref bool).
Program.exe: Program.cs
	mcs -sdk:4 -r:System.Drawing `pkg-config --libs opentk` Program.cs

