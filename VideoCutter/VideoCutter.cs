using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using System.IO.Pipes;
using System.Drawing;
using System.Drawing.Imaging;


namespace VideoCutter
{
    class VideoCutter
    {
        //Название аккаунта
        string storageAccountName = "hbgittest";
        //Ключ доступа
        string Key = "";        

        CloudStorageAccount cloudStorageAccount;                                //Объявили строку подключения
        CloudBlobClient blobClient;                                             //Объявили БлобКлиент
        CloudBlobContainer containerVideos;                                     //Объявили переменную, которая будет ссылаться на контейнер videos
        CloudBlobContainer containerAudios;                                     //Объявили переменную, которая будет ссылаться на контейнер audios
        CloudBlobContainer containerFrames;                                     //Объявили переменную, которая будет ссылаться на контейнер frames

        Process proc = new Process();                                           //Объявили процесс и инициализировали его

        MemoryStream videoStream = new MemoryStream();                          //Поток видео
        CloudBlockBlob blockBlobVideo;
        public VideoCutter()
        {
            //Инициализировали строку подключения
            cloudStorageAccount = CloudStorageAccount.Parse($"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={Key};EndpointSuffix=core.windows.net");

            blobClient = cloudStorageAccount.CreateCloudBlobClient();

            containerVideos = blobClient.GetContainerReference("videos");      //Сослались на контейнер videos в Облаке
            containerAudios = blobClient.GetContainerReference("audios");      //Сослались на контейнер audios в Облаке
            containerFrames = blobClient.GetContainerReference("frames");      //Сослались на контейнер frames в Облаке

            proc.StartInfo.FileName = @"ffmpeg.exe";
            proc.StartInfo.UseShellExecute = false;
            proc.StartInfo.RedirectStandardInput = true;
            proc.StartInfo.RedirectStandardOutput = true;
            proc.StartInfo.UseShellExecute = false;

            
        }


        /// <summary>
        /// Метод для получения видео из облака
        /// </summary>
        /// <returns></returns>
        public async void GetVideo(string name)
        {
            blockBlobVideo = containerVideos.GetBlockBlobReference(name);      //Получаем ссылку на файл в облаке            
            await blockBlobVideo.DownloadToStreamAsync(videoStream);                                //Помещаем поток байтов видеофайла в поток videoStream
            videoStream.Position = 0;                                                               //Возвращаем положение в потоке на начало            
        }

        /// <summary>
        /// Метод вызова ffmpeg.exe
        /// </summary>
        /// <param name="commandLine">Команда ffmpeg</param>
        static void ffProcessStart(string commandLine)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();            //Создаем и инициализируем новый процесс

            startInfo.CreateNoWindow = false;                                //Запускаем процесс без создания для него нового окна

            startInfo.UseShellExecute = false;                              //процесс должен создаваться непосредственно из исполняемого файла

            startInfo.FileName = @"ffmpeg.exe";                              //Программа для конвертации видео и аудио файлов

            startInfo.WindowStyle = ProcessWindowStyle.Hidden;              //Система отображает скрытое окно

            startInfo.Arguments = commandLine;                              //В качестве аргумента для ffmpeg.exe передаем команду 

            try
            {
                using (Process exeProcess = Process.Start(startInfo))       //Запускаем процесс
                {
                    exeProcess.WaitForExit();                               //Ожидаем завершение процесса
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Метод который получает поток видео из облака, извлекает из него аудио в виде потока байтов и отправляет обратно в облако
        /// </summary>
        /// <param name="fileName">Название видео файла в контейнере videos</param>
        /// <param name="outputFileName">Имя для аудиофайла, который будет помещен audios</param>
        public void BlobGetVideoPutAudio(string fileName, string outputFileName)
        {           
            string strTakeFrame = $"-i - -f wav -";                                                 //Строка получения аудиофайла из видеофайла
            //videoStream.Position = 0;
            proc.StartInfo.Arguments = strTakeFrame;
            
            CloudBlockBlob blockBlobAudio = containerAudios.GetBlockBlobReference(outputFileName);
            proc.Start();

            var inputTask = Task.Run(() =>
            {
                convertMemoryStreamToStream(videoStream, proc.StandardInput.BaseStream);
                proc.StandardInput.Close();
            });           

            var outputTask = Task.Run(() =>
            {
                var OutputStream = proc.StandardOutput.BaseStream;
                blockBlobAudio.UploadFromStream(OutputStream);
            });

            Task.WaitAll(inputTask, outputTask);
        }

        

        public void getVideoFramesFromBlob(string fileName, string outputFileName, int period)
        {
            string strTakeFrame = $"-i - -qscale:v 4 -vf fps=1/{period} -f image2pipe -";
            
            proc.StartInfo.Arguments = strTakeFrame;
            proc.Start();

            CloudBlockBlob blockBlobFrame;// = containerAudios.GetBlockBlobReference(outputFileName);

            var inputTask = Task.Run(() =>
            {
                convertMemoryStreamToStream(videoStream, proc.StandardInput.BaseStream);
                proc.StandardInput.Close();
            });

            MemoryStream MS = new MemoryStream();
            var outputTask = Task.Run(() =>
            {
                var OutputStream = proc.StandardOutput.BaseStream;
                OutputStream.CopyTo(MS);
                proc.StandardOutput.Close();
            });

            Task.WaitAll(inputTask, outputTask);
            int index = 1;
            MS.Position = 0;
            MemoryStream FrameStream;
            foreach (Image Im in GetThumbnails(MS))             
            {
                FrameStream = new MemoryStream();
                using (var im = new Bitmap(Im))
                {
                    blockBlobFrame = containerFrames.GetBlockBlobReference(String.Format(outputFileName + index + ".png"));
                    im.Save(FrameStream, ImageFormat.Png);                    
                    FrameStream.Position = 0;
                    blockBlobFrame.UploadFromStream(FrameStream);
                    //blockBlobFrame.UploadFromStream(ReturnStreamFromMemoryStream(FrameStream));

                    index++;
                }
            }
        }

        static IEnumerable<Image> GetThumbnails(Stream stream)
        {
            byte[] allImages;
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                allImages = ms.ToArray();                                               //В allImages поместили поток всех полученных изображений в виде одного массива байтов
            }
            var bof = allImages.Take(8).ToArray();                                      //В bof поместили 8 байтов из массива allImages (8 ячеек массива allImages)
            var prevOffset = -1;                                                
            foreach (var offset in GetBytePatternPositions(allImages, bof))             //Перебираем индексы начала подмассивов
            {
                if (prevOffset > -1)
                    yield return GetImageAt(allImages, prevOffset, offset);             //Возвращаем объект Image на основе под массива байтов в место вызова метода
                prevOffset = offset;
            }
            if (prevOffset > -1)
                yield return GetImageAt(allImages, prevOffset, allImages.Length);       //Вернуть оставшиеся данные массива в виде изображения
        }

        static Image GetImageAt(byte[] data, int start, int end)                        //Получили массив байтов, начальный индекс в массиве и конечный индекс в массиве
        {
            using (var ms = new MemoryStream(end - start))                              //Инициализировали поток
            {
                ms.Write(data, start, end - start);                                     //Записали в поток под последовательность байтов
                return Image.FromStream(ms);                                            //Вернули изображение в виде объекта Image
            }
        }

        static IEnumerable<int> GetBytePatternPositions(byte[] data, byte[] pattern)    //data содержит весь массив байтов с изображениями, pattern - содержит первые 8 ячеек массива data
        {
            var dataLen = data.Length;                                                  //Получили общее количество байтов в массиве
            var patternLen = pattern.Length - 1;                                        //Получили число байтов в шаблоне (8-1) = 7
            int scanData = 0;                                                           //Индекс проверенных байтов массива data
            int scanPattern = 0;                                                        //Индекс проверки байтов шаблона
            while (scanData < dataLen)                                                  //В цикле переберем все ячеки массива байтов
            {
                if (pattern[0] == data[scanData])                                       //Найдем совпадения [0] го байта шаблона в основном массиве. Если нашли первое совпадение, то
                {
                    scanPattern = 1;                                                    //индекс шаблона присваиваем 1
                    scanData++;                                                         //инкрементируем индекс основного массива
                    while (pattern[scanPattern] == data[scanData])                      //Пока последующие байты совпадают
                    {
                        if (scanPattern == patternLen)                                  //Если индекс проверки байтов шаблона равен 7, то 
                        {
                            yield return scanData - patternLen;                         //Возвращаем индекс - (как разность  scanData - 7) начала изображения в общем массиве
                            break;
                        }
                        scanPattern++;
                        scanData++;
                    }
                }
                scanData++;                                                             //Инкрементируем индекс основного массива с данными, до тех пор пока не найдем совпадение
            }
        }        

        /// <summary>
        /// Метод для извлечения Аудио дорожки из локального видеофайла
        /// </summary>
        /// <param name="fileName">Путь до файла</param>
        /// <param name="outputFileName">Название выходного файла</param>
        /// <param name="period">Период извлечения кадров</param>
        public void getAudioBitrate(string fileName, string outputFileName = "")
        {
            //-vn
            //-ar
            //-ac
            //-ab
            //-f
            //-i source_video.avi -vn -ar 44100 -ac 2 -ab 192 -f mp3 sound.mp3
            if (outputFileName == "")
                outputFileName = new StringBuilder(fileName).Replace(".", "_").ToString();

            string strTakeFrame = $"-i {fileName} -vn -ar 44100 -ac 2 -ab 192 -f mp3 {outputFileName}.mp3";       //Формируем команду

            ffProcessStart(strTakeFrame);                                                                         //Вызываем ffmpeg.exe с командой в качестве агрумента
        }

        /// <summary>
        /// Метод для извлечения кадров с периодом из локального видеофайла
        /// </summary>
        /// <param name="fileName">Путь до файла</param>
        /// <param name="outputFileName">Название выходного файла</param>
        /// <param name="period">Период извлечения кадров в секундах</param>
        public void getVideoFrames(string fileName, string outputFileName, int period)
        {
            //-i атрибут input
            //-vf атрибут video frames, установить количество видеокадров для вывода
            //fps=1/3 1 кадр каждые 3с
            string strTakeFrame = $"-i {fileName} -qscale:v 4 -vf fps=1/{period} {outputFileName}_%04d.jpg";       //Формируем команду

            ffProcessStart(strTakeFrame);
        }       

        /// <summary>
        /// Метод извлекающий из облака видеофайл из контейнера videos
        /// </summary>
        /// <param name="fileName">Название файла</param>
        /// <param name="saveToPath">Путь для сохранения файла в локальной директории</param>
        public async void downloadFileFromBlob(string fileName, string saveToPath)
        {
            //Получение ссылки на объект в облаке, находящемся в контейнере videos
            CloudBlockBlob blockBlob = containerVideos.GetBlockBlobReference(fileName);

            string path = string.Concat(saveToPath, blockBlob.Name);

            await blockBlob.DownloadToFileAsync(path, FileMode.OpenOrCreate);
        }

        /// <summary>
        /// Метод вызываемый в отдельном потоке, который отправляет видеофайл
        /// в Pipe в виде массива байтов
        /// </summary>
        /// <param name="fileName">Путь до локального видеофайла</param>
        private void putVideoInPipeLine(object fileName)
        {
            Stream video = File.OpenRead((string)fileName);                 //Инициализируем поток локальным видео файлом
            long bufferSize = video.Length;                                 //определим количество байт потока
            byte[] buffer = new byte[bufferSize];                           //инициализируем буфер с размером байтов потока
            long bytesRead = video.Read(buffer, 0, buffer.Length);          //Считываем файл в виде массива байтов

            NamedPipeServerStream pipeServer = new NamedPipeServerStream("ffpipe", PipeDirection.InOut);    //Объявим Pipe сервер

            pipeServer.WaitForConnection();                                 //Ожидаем клиента
            pipeServer.Write(buffer, 0, buffer.Length);                     //Как только клиент соединился с сервером, то отправляем массив байтов видео файла
            pipeServer.Flush();                                             //Вызываем запись данных и последующую очистку буфера
            Console.Beep();                                                 //сигнал о том что данные отправлены
        }

        /// <summary>
        /// Метод для отправки локального файла в облако в виде массива байтов
        /// </summary>
        /// <param name="imagePath">Путь до изображения</param>
        /// <param name="fileName">Новое название изображения</param>
        public async void sendImageInBlob(string imagePath, string fileName)
        {
            try
            {
                Stream Image = File.OpenRead(imagePath);                                                //Получили поток байтов изображения
                long bufferSize = Image.Length;                                                         //Определили длину потока в байтах
                byte[] buffer = new byte[bufferSize];                                                   //инициализировали буфер для приема изображения
                long bitesRead = Image.Read(buffer, 0, buffer.Length);                                  //поместили считанное изображение в буфер в виде массива байтов
                var cloudBlockBlob = containerFrames.GetBlockBlobReference(fileName);                   //Объявили cloudBlockBlob с названием fileName в контейнере containerFrames

                if (!cloudBlockBlob.Exists())                                                           //Если файла с таким именем не существует, то 
                {
                    using (Stream stream = new MemoryStream(buffer))                                    //Объявили поток в который поместили 
                    {
                        await cloudBlockBlob.UploadFromStreamAsync(stream);                             //Загружаем массив байтов в виде поока в облако
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка загрузки в облако файла: " + fileName);
            }
        }

        /// <summary>
        /// Метод, который считывает данные из pipe d в виде массива байтов и отправляет этот файл в облако
        /// </summary>
        /// <param name="Name">Название для изображений</param>
        private void getFramesFromPipeAndSendInBlob(object Name)
        {
            long buffSize;
            byte[] buff;
            NamedPipeServerStream pipeClient = new NamedPipeServerStream("result", PipeDirection.In);   //Инициализировали Pipe сервер
            pipeClient.WaitForConnection();                                                             //Ожидаем подключение к серверу

            Console.Beep();                                                                             //Сигнал что к серверу соединились
            int index = 0;
            while (true)
            {
                buffSize = pipeClient.Length;                                                           //Определяем длину потока в байтах
                buff = new byte[buffSize];                                                              //инициализируем массив байтов
                pipeClient.ReadAsync(buff, 0, buff.Length);                                             //Считываем данные в буфер

                var cloudBlockBlob = containerFrames.GetBlockBlobReference((string)Name + "_" + index); //Объявили cloudBlockBlob

                if (!cloudBlockBlob.Exists())                                                           //Если файл не существует, то
                {
                    using (Stream stream = new MemoryStream(buff))                                      //Объявили поток, в который поместили массив байтов изображения
                    {
                        cloudBlockBlob.UploadFromStreamAsync(stream);                                   //Отпрака изображения в облако
                    }
                }
            }
        }

        /// <summary>
        /// Метод преобразования типа MemoryStream в Stream
        /// </summary>
        /// <param name="MS">поток типа MemoryStream</param>
        /// <param name="S">поток типа Stream</param>
        static void convertMemoryStreamToStream(MemoryStream MS, Stream S)
        {
            byte[] buffer = new byte[32 * 1024]; // 32K buffer for example
            int bytesRead;
            while ((bytesRead = MS.Read(buffer, 0, buffer.Length)) > 0)
            {
                S.Write(buffer, 0, bytesRead);
            }
        }

        /// <summary>
        /// Метод преобразующий MemoryStream в Stream
        /// </summary>
        /// <param name="MS">Входной MemoryStream</param>
        /// <returns></returns>
        static Stream ReturnStreamFromMemoryStream(MemoryStream MS)
        {
            Stream S = new MemoryStream();
            MS.Position = 0;
            byte[] buffer = new byte[32 * 1024]; // 32K buffer for example
            int bytesRead;
            while ((bytesRead = MS.Read(buffer, 0, buffer.Length)) > 0)
            {
                S.Write(buffer, 0, bytesRead);
            }
            return S;
        }

        #region TestPipe
        public void TestPipe(string imagePath)
        {
            Thread th1 = new Thread(new ParameterizedThreadStart(sendImageInPipe));
            th1.Start(imagePath);
            Thread th2 = new Thread(new ParameterizedThreadStart(getFramesFromPipe));
            th2.Start(@"D:\VideoCutterRepo\newImage.jpg");
        }

        public void sendImageInPipe(object imagePath)
        {
            Stream Image = File.OpenRead((string)imagePath);                                        //Получили поток байтов изображения
            long bufferSize = Image.Length;                                                         //Определили длину потока в байтах
            byte[] buffer = new byte[bufferSize];                                                   //инициализировали буфер для приема изображения
            long bitesRead = Image.Read(buffer, 0, buffer.Length);                                  //поместили считанное изображение в буфер в виде массива байтов

            NamedPipeServerStream pipeServer = new NamedPipeServerStream("ImagePipe", PipeDirection.InOut);
            pipeServer.WaitForConnection();
            pipeServer.WriteAsync(buffer, 0, buffer.Length);
            pipeServer.Flush();
            Console.Beep();
            Thread.Sleep(1000);
            pipeServer.Close();            
        }

        private void getFramesFromPipe(object Name)
        {
            long buffSize;
            byte[] buff;
            NamedPipeClientStream pipeClient = new NamedPipeClientStream("ImagePipe");                  //Инициализировали Pipe сервер
            pipeClient.Connect();                                                                       //Ожидаем подключение к серверу

            Console.Beep();                                                                             //Сигнал что к серверу соединились
            int index = 0;
            while (true)
            {
                //buffSize = pipeClient.Length;                                                         //Определяем длину потока в байтах
                buff = new byte[65537];                                                                 //инициализируем массив байтов
                index = pipeClient.Read(buff, 0, buff.Length);                                          //Считываем данные в буфер
                if (index > 0)                    
                    File.WriteAllBytes((string)Name, buff);                                             //Создаем новый файл изображения из потока байтов     
                else
                    break;
            }
        }
        #endregion TestPipe
    }
}
