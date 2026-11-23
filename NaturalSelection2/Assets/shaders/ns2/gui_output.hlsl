PS_OUTPUT MainPs( PS_INPUT i )
{
	UI_CommonProcessing_Pre( i );
	PS_OUTPUT o;
	o.vColor = i.vColor;
	MaterialShader( g_flTextured != 0, i.vTexCoord.xy, i.vColor, o.vColor );
	return UI_CommonProcessing_Post( i, o );
}
