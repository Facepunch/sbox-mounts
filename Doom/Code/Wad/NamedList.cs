using System;
using System.Collections;

namespace Doom.Wad;

/// <summary>
/// Like a case-insensitive dictionary, but preserves the order of insertion and allows duplicate names.
/// Also has a fast index lookup for the last entry with a given name, which is how Doom defines texture animations.
/// </summary>
public sealed class NamedList<T> : IReadOnlyList<T> where T : class
{
	private readonly Func<T, string> _nameOf;
	private readonly List<T> _items = [];
	private readonly Dictionary<string, int> _byName = new( StringComparer.OrdinalIgnoreCase );

	internal NamedList( Func<T, string> nameOf ) => _nameOf = nameOf;

	internal void Add( T item )
	{
		_byName[_nameOf( item )] = _items.Count;
		_items.Add( item );
	}

	public int Count => _items.Count;

	public T this[int index] => _items[index];

	public T Get( string name ) => name is not null && _byName.TryGetValue( name, out var i ) ? _items[i] : null;

	public bool TryGet( string name, out T value )
	{
		value = Get( name );
		return value is not null;
	}

	public bool Contains( string name ) => name is not null && _byName.ContainsKey( name );

	/// <summary>Position of the last entry with this name, or -1. Doom's animation ranges are defined by these positions.</summary>
	public int IndexOf( string name ) => name is not null && _byName.TryGetValue( name, out var i ) ? i : -1;

	public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
