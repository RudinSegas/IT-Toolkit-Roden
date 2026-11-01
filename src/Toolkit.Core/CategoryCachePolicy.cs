using System;
namespace Toolkit.Core
{
    public static class CategoryCachePolicy
    {
        public static bool Fresh(string timestamp,DateTimeOffset now){DateTimeOffset stamp;return DateTimeOffset.TryParse(timestamp,out stamp)&&now>=stamp&&(now-stamp).TotalSeconds<120;}
        public static bool Accept(long requested,long current,string category,string selected){return requested==current&&category==selected;}
    }
}
