/* TSBindingsGenerator Generated code -- this code is regenerated on each build */
namespace SkiaSharp
{
	export class sk_filestream_new_0_Params
	{
		/* Pack=4 */
		utf8path_Length : number;
		utf8path : Array<number>;
		public static unmarshal(pData:number, memoryContext: any = null) : sk_filestream_new_0_Params
		{
			memoryContext = memoryContext ? memoryContext : Module;
			let ret = new sk_filestream_new_0_Params();
			
			{
				ret.utf8path_Length = Number(memoryContext.getValue(pData + 0, "i32"));
			}
			
			{
				var pArray = memoryContext.getValue(pData + 4, "*");
				if(pArray !== 0)
				{
					ret.utf8path = new Array<number>();
					for(var i=0; i<ret.utf8path_Length; i++)
					{
						var value = memoryContext.getValue(pArray + i * 4, "i8");
						ret.utf8path.push(Number(value));
					}
				}
				else
				
				{
					ret.utf8path = null;
				}
			}
			return ret;
		}
	}
}