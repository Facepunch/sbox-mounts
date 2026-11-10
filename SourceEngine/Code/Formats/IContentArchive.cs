using System;

namespace Source1;

internal interface IContentArchive : IDisposable
{
	IEnumerable<string> GetFilePaths();
	Stream OpenFile( string path );
	Stream OpenFileHeader( string path, int length ) => OpenFile( path );
}
