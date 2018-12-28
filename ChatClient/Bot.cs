using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ChatClient
{
    static class Bot
    {
        static Bot()
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (Stream stream = assembly.GetManifestResourceStream("ChatClient.names.txt"))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();
                Names = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            }

            using (Stream stream = assembly.GetManifestResourceStream("ChatClient.sentences.txt"))
            using (StreamReader reader = new StreamReader(stream))
            {
                string result = reader.ReadToEnd();
                Sentences = result.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            }
        }

        public static string[] Sentences { get; }

        public static string[] Names { get; }

        public static string RandomName()
        {
            return Names[new Random().Next(Names.Length)];
        }

        public static string RandomSentence()
        {
            return Sentences[new Random().Next(Sentences.Length)];
        }
    }
}
