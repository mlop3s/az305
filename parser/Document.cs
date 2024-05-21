namespace Company.Function
{
    internal class Document
    {
        public string id { get; set; }
        public string name { get; internal set; }
        public string content { get; internal set; }
        public string reference { get; internal set; }
        public string userId { get; internal set; }
        public string social { get; internal set; }
    }
}