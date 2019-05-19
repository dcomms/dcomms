 
namespace Dcomms.P2PTP
{	
		public static class CompilationInfo
		{
			public static uint CompilationDateTimeUtc_uint32
			{
				get
				{
					return 11981170;
				}
			}
			public static System.DateTime ToDateTime(uint seconds)
			{				
				return new System.DateTime(2019,01,01).AddSeconds(seconds);				
			}
			public static System.DateTime CompilationDateTimeUtc
			{
				get
				{
					return ToDateTime(CompilationDateTimeUtc_uint32);
				}
			}
			public static string CompilationDateTimeUtcStr
			{
				get
				{
					return string.Format("{0:yyMMdd-HH:mm}", CompilationDateTimeUtc);
				}
			}
		}
}
