module Sample

open SourceCodeTypeProvider

type Foo = CSharp<"""
	public static class Foo {
		public static string Bar = "Hello from Foo!";
	}
""">

type Bar = CSharp<"""
	public static class Bar {
		public static int Baz => 42;
	}
""">

printfn "Foo.Bar: %s" Foo.Bar
printfn "Bar.Baz: %d" Bar.Baz
