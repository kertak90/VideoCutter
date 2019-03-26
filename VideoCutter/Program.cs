using System;
using System.Threading;



namespace VideoCutter
{
    #region Описание задания
    //Задание 1.
    //Написать скрипт, который бы извлекал аудио из видео test.mp4 и сохранял его 
    //в контейнер audios в формате wav.При этом запрещено сохранять любую информацию 
    //о файлах локально (любое локальное сохранение файлов или информации о них 
    //в решении будет расцениваться, как неправильное решение).

    //Задание 2.
    //Написать скрипт, который бы извлекал каждые три секунды кадр из видео test.mp4 
    //и сохранял их в контейнер frames.При этом запрещено сохранять любую информацию 
    //о файлах локально(любое локальное сохранение файлов или информации о них 
    //в решении будет расцениваться, как неправильное решение).
    #endregion Описание задания


    class Program
    {
        static void Main(string[] args)
        {
            string output = $"D:\\VideoCutterRepo\\{DateTime.Now.Ticks}";
            VideoCutter vC = new VideoCutter();
            vC.GetVideo("test.mp4");            
            Thread.Sleep(7000);

            vC.getVideoFramesFromBlob("test.mp4", "testFrame", 3);                                          //Работает
            vC.BlobGetVideoPutAudio(@"test.mp4", $"test_mp4.wav");                                          //Работает

            #region Проверка
            //vC.downloadFileFromBlob("test.mp4", $"D:\\VideoCutterRepo\\MyFile\\");                        //Работает
            //vC.getVideoFrames(@"D:\VideoCutterRepo\test.mp4", output, 3);                                 //Работает локально
            //vC.getAudioBitrate(@"D:\VideoCutterRepo\test.mp4");                                           //Работает локально            
            //vC.sendImageInBlob(@"D:\VideoCutterRepo\636889819400856041_0001.jpg", "OVP_Image.jpg");       //Работает   

            //Чтение изображения из локальной папки, в первом потоке отправка изображения 
            //в виде массива байтов в Pipe, во втором потоке принимаем массив байтов из pipe и сохраняем 
            //в локальную папку
            //vC.TestPipe(@"D:\VideoCutterRepo\636889819400856041_0001.jpg");                               
            #endregion Проверка

            Console.ReadLine();
        }

    }
}
