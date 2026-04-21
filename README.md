⏺ # RAG Example                                                                                                                                                                                                                                                                                                           
                                                                                                                                                                                                                                                                                                                          
  A basic Retrieval-Augmented Generation (RAG) system built with .NET 10. It loads a company return policy document, chunks and embeds it using a local Ollama model, stores the embeddings in SQLite, and answers natural language questions against that data using a local LLM.                                        
   
  ## How it works                                                                                                                                                                                                                                                                                                         
                  
  1. On startup, the policy document is split into chunks and embedded using `nomic-embed-text` via Ollama                                                                                                                                                                                                                
  2. Embeddings are stored in a local SQLite database (skipping any chunks already stored)
  3. When a question comes in, it is embedded and compared against stored chunks using cosine similarity                                                                                                                                                                                                                  
  4. The top 3 most relevant chunks are passed as context to `llama3.2` to generate an answer                                                                                                                                                                                                                             
                                                                                                                                                                                                                                                                                                                          
  ## Tech stack                                                                                                                                                                                                                                                                                                           
                                                                                                                                                                                                                                                                                                                          
  - **.NET 10** — ASP.NET Core Web API                                                                                                                                                                                                                                                                                    
  - **Ollama** — local LLM inference (`llama3.2` for chat, `nomic-embed-text` for embeddings)
  - **OllamaSharp** — .NET client for the Ollama API                                                                                                                                                                                                                                                                      
  - **SQLite** — lightweight local storage for embeddings via `Microsoft.Data.Sqlite`
  - **Scalar** — API documentation and testing UI                                                                                                                                                                                                                                                                         
                                                                                                                                                                                                                                                                                                                          
  ## Requirements                                                                                                                                                                                                                                                                                                         
                                                                                                                                                                                                                                                                                                                          
  - [.NET 10 SDK](https://dotnet.microsoft.com/download)                                                                                                                                                                                                                                                                  
  - [Ollama](https://ollama.com) running locally on `http://localhost:11434`
  - The following models pulled in Ollama:                                                                                                                                                                                                                                                                                
    ollama pull llama3.2                                                                                                                                                                                                                                                                                                  
    ollama pull nomic-embed-text                                                                                                                                                                                                                                                                                          
                                                                                                                                                                                                                                                                                                                          
  ## Getting started                                                                                                                                                                                                                                                                                                      
                  
  1. Clone the repository
  2. Ensure Ollama is running
  3. Run the project:
     dotnet run                                                                                                                                                                                                                                                                                                           
  4. Open the Scalar UI to test the API:
     http://localhost:5134/scalar/v1                                                                                                                                                                                                                                                                                      
                  
  ## API
                                                                                                                                                                                                                                                                                                                          
  ### `POST /policy/ask`
                                                                                                                                                                                                                                                                                                                          
  Ask a question against the policy document.

  **Request body:**                                                                                                                                                                                                                                                                                                       
  ```json
  {                                                                                                                                                                                                                                                                                                                       
    "question": "What is your return policy?"
  }

  Response:
  {
    "answer": "..."
  }
         
