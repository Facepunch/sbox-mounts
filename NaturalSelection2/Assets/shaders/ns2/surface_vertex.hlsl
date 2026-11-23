#include "common/vertex.hlsl"

PixelInput MainVs( VertexInput v )
{
	PixelInput i = ProcessVertex( v );
	i.vColor = v.vColor;
	return FinalizeVertex( i );
}
