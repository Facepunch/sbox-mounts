using System;
using System.Globalization;
using System.Net;

namespace Overgrowth;

internal sealed class XmlAsset
{
	public string Name { get; }
	public string Value { get; private set; } = "";
	public List<XmlAsset> Children { get; } = [];
	readonly Dictionary<string,string> attributes = new();
	XmlAsset( string name ) => Name = name;
	public string Attribute( string key ) => attributes.GetValueOrDefault( key );
	public float? Number( string key ) => float.TryParse( Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) ? value : null;
	public XmlAsset Element( string name ) => Children.FirstOrDefault( n => n.Name == name );
	public XmlAsset RequireElement( string name ) => Element( name )
		?? throw new InvalidDataException( $"Missing <{name}> in <{Name}>." );
	public IEnumerable<XmlAsset> Elements( string name ) => Children.Where( n => n.Name == name );
	public static XmlAsset Read( BaseFileSystem fs, string path, string name ) => Parse(fs.ReadAllText(path)).Element(name)
		?? throw new InvalidDataException($"No {name} element in {path}");

	public static XmlAsset Parse( string text )
	{
		if ( text is null ) throw new InvalidDataException( "Missing Overgrowth XML data." );
		var root = new XmlAsset("");
		var nodes = new Stack<XmlAsset>();
		nodes.Push(root);
		int i = 0;
		void Space() { while(i<text.Length && char.IsWhiteSpace(text[i])) i++; }
		string Name()
		{
			int start=i;
			while(i<text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('/' or '>' or '=')) i++;
			if(start==i) throw new InvalidDataException($"Missing XML name at {i}");
			return text[start..i];
		}
		int End(string token)
		{
			int end=text.IndexOf(token,i,StringComparison.Ordinal);
			if(end<0) throw new InvalidDataException($"Missing XML terminator {token} at {i}");
			return end;
		}
		while(i<text.Length)
		{
			if(text[i]!='<')
			{
				int end=text.IndexOf('<',i); if(end<0) end=text.Length;
				var value=text.AsSpan(i,end-i).Trim();
				if(!value.IsEmpty) nodes.Peek().Value+=WebUtility.HtmlDecode(value.ToString());
				i=end;continue;
			}
			if(text.AsSpan(i).StartsWith("<!--")) {i=End("-->")+3;continue;}
			if(text.AsSpan(i).StartsWith("<?")) {i=End("?>")+2;continue;}
			if(text.AsSpan(i).StartsWith("<![CDATA[")) {i+=9;int end=End("]]>");nodes.Peek().Value+=text[i..end];i=end+3;continue;}
			i++;
			if(i<text.Length && text[i]=='/')
			{
				i++;var name=Name();Space();
				if(nodes.Count<=1 || nodes.Pop().Name!=name || i>=text.Length || text[i++]!='>') throw new InvalidDataException($"Invalid closing tag {name}");
				continue;
			}
			var node=new XmlAsset(Name());nodes.Peek().Children.Add(node);
			for(;;)
			{
				Space();if(i>=text.Length) throw new InvalidDataException("Unterminated element");
				if(text[i]=='>') {i++;nodes.Push(node);break;}
				if(text.AsSpan(i).StartsWith("/>")) {i+=2;break;}
				var key=Name();Space();
				if(i>=text.Length || text[i++]!='=') throw new InvalidDataException($"Missing = after {key}");
				Space();
				if ( i == text.Length ) throw new InvalidDataException( $"Missing value for {key}." );
				if(text[i] is not ('\'' or '"')) { int start=i; while(i<text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not ('/' or '>'))i++; node.attributes[key]=text[start..i]; continue; }
				var quote=text[i++];int end=text.IndexOf(quote,i);
				if(end<0)throw new InvalidDataException($"Unterminated {key}");
				node.attributes[key]=WebUtility.HtmlDecode(text[i..end]);i=end+1;
			}
		}
		if(nodes.Count!=1) throw new InvalidDataException("Incomplete XML fragment");
		return root;
	}
}
