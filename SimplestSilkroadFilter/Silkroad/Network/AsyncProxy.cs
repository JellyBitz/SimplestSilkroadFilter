namespace Silkroad.Network
{
    public class AsyncProxy
    {
        #region Public Properties
        /// <summary>
        /// The local connection to outside world (Client to Proxy)
        /// </summary>
        public AsyncClient Server { get; set; }
        /// <summary>
        /// The remote connection to the game server (Proxy to Server)
        /// </summary>
        public AsyncClient Client { get; set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Default constructor
        /// </summary>
        public AsyncProxy()
        {

        }
        #endregion
    }
}