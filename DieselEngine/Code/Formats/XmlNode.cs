using System.Net;
using System.Text.RegularExpressions;

namespace Diesel;

/// <summary>Small reader for Diesel's element/attribute-only unit and object descriptions.</summary>
sealed class XmlNode
{
	public string Name { get; private set; }
	readonly Dictionary<string, string> attributes = [];
	readonly List<XmlNode> children = [];
	public string Attribute( string name ) => attributes.GetValueOrDefault( name );
	public XmlNode Element( string name ) => children.FirstOrDefault( c => c.Name == name );
	public IEnumerable<XmlNode> Elements( string name ) => children.Where( c => c.Name == name );

	public static XmlNode Parse( string text )
	{
		text = Regex.Replace( text, @"<!--[\s\S]*?-->", "" );
		var root = new XmlNode { Name = "" };
		var stack = new Stack<XmlNode>();
		stack.Push( root );
		foreach ( Match tag in Regex.Matches( text, @"<(/?)([\w:.-]+)((?:[^>""']|""[^""]*""|'[^']*')*)>" ) )
		{
			string name = tag.Groups[2].Value;
			if ( tag.Groups[1].Value == "/" )
			{
				if ( stack.Count < 2 || stack.Pop().Name != name ) throw new InvalidDataException( "Mismatched Diesel XML element." );
				continue;
			}
			var node = new XmlNode { Name = name };
			foreach ( Match attr in Regex.Matches( tag.Groups[3].Value, """([\w:.-]+)\s*=\s*(?:"([^"]*)"|'([^']*)')""" ) )
				node.attributes[attr.Groups[1].Value] = WebUtility.HtmlDecode( attr.Groups[2].Success ? attr.Groups[2].Value : attr.Groups[3].Value );
			stack.Peek().children.Add( node );
			if ( !tag.Groups[3].Value.TrimEnd().EndsWith( "/" ) ) stack.Push( node );
		}
		if ( stack.Count != 1 || root.children.Count != 1 ) throw new InvalidDataException( "Invalid Diesel XML document." );
		return root.children[0];
	}
}
