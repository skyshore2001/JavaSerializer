# JavaSerializer

This is a fork of https://github.com/GitKepler/JavaSerializer

CHANGLOG:

- bugfix: byte order of float/double data
- bugfix: read flags (ClassDescFlag)
- support fields of types like `List<double[]>`.
implement objectAnnatation grammer.

	classdata:
		objectAnnotation
	objectAnnotation:
		contents endBlockData

- add java test project `jtest1` and C# demo project `test1`. `jtest1` write data file `test1.bin` and `test1` read from the data file.

