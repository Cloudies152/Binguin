using Verse;

namespace Binguin.Util
{
    /// <summary>
    /// 按名称懒加载 Def。由实例隔离缓存；是否缓存缺失结果由调用方决定。
    /// 不使用泛型静态字典，避免不同 Def 名称共享错误的缓存。
    /// </summary>
    internal sealed class DefLookup<T> where T : Def
    {
        private readonly string defName;
        private readonly bool retryIfMissing;
        private T cachedDef;
        private bool lookedUp;

        public DefLookup(string defName, bool retryIfMissing)
        {
            this.defName = defName;
            this.retryIfMissing = retryIfMissing;
        }

        public T Value
        {
            get
            {
                if (!lookedUp || (retryIfMissing && cachedDef == null))
                {
                    lookedUp = true;
                    cachedDef = DefDatabase<T>.GetNamedSilentFail(defName);
                }
                return cachedDef;
            }
        }
    }
}
