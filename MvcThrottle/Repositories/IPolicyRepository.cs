namespace MvcThrottle.Repositories
{
    public interface IPolicyRepository
    {
        ThrottlePolicy FirstOrDefault(string id);

        void Remove(string id);

        void Save(string id, ThrottlePolicy policy);
    }
}