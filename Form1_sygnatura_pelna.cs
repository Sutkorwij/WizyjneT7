using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.Structure;
using System.Threading;

namespace SW_T7
{
    public partial class Form1_sygnatura_pelna : Form
    {
        //Deklaracje nowych typow danych
        private delegate double Filtr(double[] data, FilterParams param);
        private enum TrybRysowania { NAD_KRZYWA, TYLKO_DANE, TYLKO_KRZYWA }
        //Zmienne o typach pochodzących z powyższych deklaracji lub bibliotek zewnętrznych
        private Image<Bgr, byte> image_PB1, image_PB2, image_PB3, image_copy;
        private MCvScalar kolor_start, kolor_stop;
        private FilterParams parametry_filtru;
        private Filtr wybrany_filtr;
        //Zmienne o typach dostępnych w C#
        private Random rnd = new Random();
        private Size desired_image_size;
        private int liczba_promieni, opoznienie_rysowania, kat_poczatkowy;
        private double margines_na_tekst = 40;
        private double[] tabela_promieni;
        private double[] tabela_wartosci_srednich;
        private Point srodek_ciezkosci;
        bool draw_example_option_selector = false;
        bool draw_example_abort_signal = false;
        Queue<Point> pix_tlace = new Queue<Point>();
        Queue<Point> pix_palace = new Queue<Point>();
        Queue<Point> pix_nadpalone = new Queue<Point>();
        Queue<Point> pix_wypalone = new Queue<Point>();
        private MCvScalar aktualnie_klikniety = new MCvScalar(0, 0, 0);
        private MCvScalar cecha_palnosci = new MCvScalar(0xFF, 0xFF, 0xFF);
        private MCvScalar cecha_nadpalenia = new MCvScalar(0, 0, 0);
        private MCvScalar kolor_tlenia = new MCvScalar(51, 153, 255);
        private MCvScalar kolor_palenia = new MCvScalar(0, 0, 204);
        private MCvScalar kolor_nadpalenia = new MCvScalar(51, 204, 51);
        private MCvScalar kolor_wypalenia = new MCvScalar(100, 100, 100);
        private MCvScalar aktualny_kolor_wypalenia = new MCvScalar(100, 100, 100);
        private bool skos = true;
        private bool cecha_dowolna = true;
        private int nr_pozaru = 0;

        public Form1_sygnatura_pelna()
        {
            InitializeComponent();
            refresh_GUI_settings();

            desired_image_size = pictureBox1.Size;
            image_PB1 = new Image<Bgr, byte>(desired_image_size);
            image_PB2 = new Image<Bgr, byte>(desired_image_size);
            image_PB3 = new Image<Bgr, byte>(pictureBox3.Size);
            image_copy = new Image<Bgr, byte> (desired_image_size);

            //Kolor start i stop - wykorzystywane do rysowania przykładowych promieni
            kolor_start = new MCvScalar(0, 255, 0);
            kolor_stop = new MCvScalar(0, 0, 255);

            parametry_filtru = new FilterParams();
        }

        private void button_Draw_example_rays_Click(object sender, EventArgs e)
        {
            //Maluje przykładowy zestaw promieni
            if (!draw_example_option_selector)
            {
                czysc_obraz(image_PB1, pictureBox1);
                button_Draw_example_rays.Text = "Przerwij malowanie promieni.";
                Application.DoEvents();
                draw_example_option_selector = true;
                draw_example_rays();
                draw_example_option_selector = false;
            }
            else
            {
                draw_example_abort_signal = true;
            }
        }

        private void button_Browse_Files_PB1_Click(object sender, EventArgs e)
        {
            textBox_Image_Path_PB1.Text = get_image_path();
        }

        private void button_From_File_PB1_Click(object sender, EventArgs e)
        {
            pictureBox1.Image = get_image_bitmap_from_file(textBox_Image_Path_PB1.Text, ref image_PB1);
        }

        private void pictureBox1_Click(object sender, EventArgs e)
        {
            //Rysuje sygnature w miejscu klikniecia
            MouseEventArgs me = e as MouseEventArgs;

            // Segmentacja i mechanika
            czysc_obraz(image_PB2, pictureBox2);
            int X, Y;
            byte[,,] temp = image_PB1.Data;
            byte[,,] copy = image_PB1.Data;
            X = Convert.ToInt32(me.X);
            Y = Convert.ToInt32(me.Y);

            if (Sprawdz_czy_cecha_palnosci(temp[Y, X, 0], temp[Y, X, 1], temp[Y, X, 2]))
            {
                pix_tlace.Enqueue(new Point(X, Y));
                temp[Y, X, 0] = (byte)kolor_tlenia.V0;
                temp[Y, X, 1] = (byte)kolor_tlenia.V1;
                temp[Y, X, 2] = (byte)kolor_tlenia.V2;
            }

            image_PB1.Data = temp;

            Cykl_Pozaru();
            Narysuj_wybrany_obiekt(0);
            Mechanika();
            czysc_obraz(image_PB2, pictureBox2);

            // Sygnatura radialna
            tabela_promieni = sygnatura_radialna(srodek_ciezkosci);
            czysc_obraz(image_PB3, pictureBox3);
            namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);

            usrednianie_wykresu();
            listView1.Items.Add("Punkt kliknięcia: " + me.Location.ToString());
            listView1.Items.Add("Minimum: " + tabela_promieni.Min());
            listView1.Items.Add("Maksimum: " + tabela_promieni.Max());
            listView1.Items.Add("Wartość średnia: " + tabela_wartosci_srednich[1]);
            listView1.Items.Add("Środek ciężkości: " + srodek_ciezkosci.X + ", " + srodek_ciezkosci.Y);

            czysc_obraz(image_PB1, pictureBox1);
            image_PB1.Data = copy;
            pictureBox1.Image = image_PB1.Bitmap;
        }

        private void pictureBox1_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                CvInvoke.Circle(image_PB1, e.Location, 10, new MCvScalar(255, 255, 255), -1);
                pictureBox1.Image = image_PB1.Bitmap;
            }
        }

        private void button_Raycast_average_Click(object sender, EventArgs e)
        {
            //Uśrednia dane sygnatury
            usrednianie_wykresu();
        }

        private void button_Redraw_raycast_Click(object sender, EventArgs e)
        {
            //Przemalowanie wykresu sygnatury
            czysc_obraz(image_PB3, pictureBox3);
            namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);
        }

        private void button_Diff_raycast_Click(object sender, EventArgs e)
        {
            //Wyznaczenie i wyswietlenie sygnatury roznicowej
            tabela_promieni = sygnatura_roznicowa(tabela_promieni, 20);
            czysc_obraz(image_PB3, pictureBox3);
            namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);

            usrednianie_wykresu();
        }

        private void button_Count_vertices_Click(object sender, EventArgs e)
        {
            //Zliczanie ilości wierzchołków figury
            czysc_obraz(image_PB3, pictureBox3);
            namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);

            usrednianie_wykresu();

            licz_wierzcholki(tabela_promieni, tabela_wartosci_srednich);
        }

        private void button_Filter_average_Click(object sender, EventArgs e)
        {
            //Filtruje dane sygnatury za pomocą filtru uśredniającego
            odswiez_parametry_filtru();
            wybrany_filtr = filtr_usredniajacy;
            filtruj_tabele(ref tabela_promieni, wybrany_filtr, parametry_filtru);
            czysc_obraz(image_PB3, pictureBox3);
            namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);
        }

        private void button_Filter_minmax_Click(object sender, EventArgs e)
        {
            //Filtruje dane sygnatury za pomocą filtru minmax
            odswiez_parametry_filtru();
            wybrany_filtr = filtr_min_max;
            filtruj_tabele(ref tabela_promieni, wybrany_filtr, parametry_filtru);
            czysc_obraz(image_PB3, pictureBox3);
            namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);
        }

        private void button_Filter_binary_Click(object sender, EventArgs e)
        {
            //Filtruje dane sygnatury za pomocą filtru binarnego. Wymaga wcześniejszego
            //uśrednienia danych sygnatury
            odswiez_parametry_filtru();

            if (parametry_filtru.binary_thresh == null)
            {
                MessageBox.Show("Przed zastosowaniem filtru binarnego należy usrednić sygnaturę");
                return;
            }
            wybrany_filtr = filtr_binarny;
            filtruj_tabele(ref tabela_promieni, wybrany_filtr, parametry_filtru);
            if(checkBo_Binary_dont_clr.Checked)
            {
                namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(0, 255, 255), TrybRysowania.TYLKO_DANE);
            }
            else
            {
                czysc_obraz(image_PB3, pictureBox3);
                namaluj_dane_z_tabeli(tabela_promieni, null, new MCvScalar(255, 0, 0), TrybRysowania.TYLKO_DANE);
            }
        }


        private void numericUpDown_Ray_count_ValueChanged(object sender, EventArgs e)
        {
            refresh_GUI_settings();
        }

        private void numericUpDown_Ray_slowdown_ValueChanged(object sender, EventArgs e)
        {
            refresh_GUI_settings();
        }

        private void numericUpDown_Start_angle_ValueChanged(object sender, EventArgs e)
        {
            refresh_GUI_settings();
        }

        private void button_Czysc_Click(object sender, EventArgs e)
        {
            czysc_obraz(image_PB1, pictureBox1);
        }

        private void button_Czysc2_Click(object sender, EventArgs e)
        {
            czysc_obraz(image_PB2, pictureBox2);
        }

        private void button_Czysc3_Click(object sender, EventArgs e)
        {
            czysc_obraz(image_PB3, pictureBox3);
        }

        #region Segmentacja i mechanika

        private void Cykl_Pozaru()
        {
            nr_pozaru++;
            do
            {
                Krok_Pozaru();
            } while (pix_tlace.Count > 0);
        }

        private void Krok_Pozaru()
        {
            byte[,,] temp = image_PB1.Data;

            Tlace_do_palacych(temp);

            foreach (Point pix in pix_palace)
            {
                Tlenie_od_palacego(temp, pix);
            }

            foreach (Point pix in pix_palace)
            {
                Nadpalenie_palacego(temp, pix);
            }

            Wypalenie_palacego(temp);

            image_PB1.Data = temp;
            pictureBox1.Image = image_PB1.Bitmap;
            Application.DoEvents();
        }

        private void Tlace_do_palacych(byte[,,] temp)
        {
            while (pix_tlace.Count > 0)
            {
                Point p = pix_tlace.Dequeue();
                pix_palace.Enqueue(p);
                temp[p.Y, p.X, 0] = (byte)kolor_palenia.V0;
                temp[p.Y, p.X, 1] = (byte)kolor_palenia.V1;
                temp[p.Y, p.X, 2] = (byte)kolor_palenia.V2;
            }
        }

        private void Tlenie_od_palacego(byte[,,] temp, Point pix_in)
        {
            if (Czy_piksel_w_zakresie(pix_in))
            {
                Point[] sasiedzi = Wylicz_wspolrzedne_sasiednich_pikseli(pix_in);
                foreach (Point p in sasiedzi)
                {
                    if (Sprawdz_czy_cecha_palnosci(temp[p.Y, p.X, 0], temp[p.Y, p.X, 1], temp[p.Y, p.X, 2]))
                    {
                        pix_tlace.Enqueue(new Point(p.X, p.Y));
                        temp[p.Y, p.X, 0] = (byte)kolor_tlenia.V0;
                        temp[p.Y, p.X, 1] = (byte)kolor_tlenia.V1;
                        temp[p.Y, p.X, 2] = (byte)kolor_tlenia.V2;
                    }
                }
            }
        }

        private void Nadpalenie_palacego(byte[,,] temp, Point pix_in)
        {
            if (Czy_piksel_w_zakresie(pix_in))
            {
                Point[] sasiedzi = Wylicz_wspolrzedne_sasiednich_pikseli(pix_in);
                bool nalezy_nadpalic = false;
                foreach (Point p in sasiedzi)
                {
                    if (cecha_dowolna)
                        nalezy_nadpalic = Sprawdz_czy_jakiekolwiek_nadpalenie(temp[p.Y, p.X, 0], temp[p.Y, p.X, 1], temp[p.Y, p.X, 2]);
                    else
                        nalezy_nadpalic = Sprawdz_czy_cecha_nadpalenia(temp[p.Y, p.X, 0], temp[p.Y, p.X, 1], temp[p.Y, p.X, 2]);
                    if (nalezy_nadpalic)
                    {
                        pix_nadpalone.Enqueue(new Point(p.X, p.Y));
                        temp[p.Y, p.X, 0] = (byte)kolor_nadpalenia.V0;
                        temp[p.Y, p.X, 1] = (byte)kolor_nadpalenia.V1;
                        temp[p.Y, p.X, 2] = (byte)kolor_nadpalenia.V2;
                    }
                }
            }
        }

        private void Wypalenie_palacego(byte[,,] temp)
        {
            while (pix_palace.Count > 0)
            {
                Point p = pix_palace.Dequeue();
                pix_wypalone.Enqueue(p);
                temp[p.Y, p.X, 0] = (byte)(aktualny_kolor_wypalenia.V0);
                temp[p.Y, p.X, 1] = (byte)(aktualny_kolor_wypalenia.V1);
                temp[p.Y, p.X, 2] = (byte)(aktualny_kolor_wypalenia.V2);
            }
        }

        private Point[] Wylicz_wspolrzedne_sasiednich_pikseli(Point pix_in)
        {
            List<Point> sasiedzi = new List<Point>();
            sasiedzi.Add(new Point(pix_in.X - 1, pix_in.Y));
            sasiedzi.Add(new Point(pix_in.X + 1, pix_in.Y));
            sasiedzi.Add(new Point(pix_in.X, pix_in.Y - 1));
            sasiedzi.Add(new Point(pix_in.X, pix_in.Y + 1));
            if (skos)
            {
                sasiedzi.Add(new Point(pix_in.X - 1, pix_in.Y - 1));
                sasiedzi.Add(new Point(pix_in.X + 1, pix_in.Y + 1));
                sasiedzi.Add(new Point(pix_in.X - 1, pix_in.Y + 1));
                sasiedzi.Add(new Point(pix_in.X + 1, pix_in.Y - 1));
            }
            return sasiedzi.ToArray();
        }

        private bool Czy_piksel_w_zakresie(Point pix_in)
        {
            int max_W, max_H;
            max_W = desired_image_size.Width - 1;
            max_H = desired_image_size.Height - 1;
            if (pix_in.X > 0 && pix_in.X < max_W && pix_in.Y > 0 && pix_in.Y < max_H)
                return true;
            else
                return false;
        }

        private bool Sprawdz_czy_cecha_palnosci(byte B, byte G, byte R)
        {
            if (B == cecha_palnosci.V0 && G == cecha_palnosci.V1 && R == cecha_palnosci.V2)
                return true;
            else
                return false;
        }

        private bool Sprawdz_czy_cecha_nadpalenia(byte B, byte G, byte R)
        {
            if (B == cecha_nadpalenia.V0 && G == cecha_nadpalenia.V1 && R == cecha_nadpalenia.V2)
                return true;
            else
                return false;
        }

        private bool Sprawdz_czy_jakiekolwiek_nadpalenie(byte B, byte G, byte R)
        {
            if (B == cecha_palnosci.V0 && G == cecha_palnosci.V1 && R == cecha_palnosci.V2)
                return false;
            else if (B == cecha_nadpalenia.V0 && G == cecha_nadpalenia.V1 && R == cecha_nadpalenia.V2)
                return true;
            else if (B == kolor_tlenia.V0 && G == kolor_tlenia.V1 && R == kolor_tlenia.V2)
                return false;
            else if (B == kolor_nadpalenia.V0 && G == kolor_nadpalenia.V1 && R == kolor_nadpalenia.V2)
                return false;
            else if (B == kolor_palenia.V0 && G == kolor_palenia.V1 && R == kolor_palenia.V2)
                return false;
            else if (B == aktualny_kolor_wypalenia.V0 && G == aktualny_kolor_wypalenia.V1 && R == aktualny_kolor_wypalenia.V2)
                return false;
            else
                return true;
        }

        private void Mechanika()
        {
            //Reczne liczenie
            double F, Sx, Sy, x0, y0;
            double Jx, Jy, Jxy;
            F = Sx = Sy =  Jx = Jy = Jxy = 0;

            //Odciecie ewentualnego stykania sie z krawedzia obrazu
            CvInvoke.Rectangle(image_PB2, new Rectangle(0, 0, desired_image_size.Width, desired_image_size.Height), new MCvScalar(0, 0, 0), 2);
            pictureBox2.Image = image_PB2.Bitmap;
            Application.DoEvents();

            //Wyliczenie momentow 1 i 2 stopnia
            byte[,,] temp = image_PB2.Data;
            for (int X = 0; X < desired_image_size.Width; X++)
            {
                for (int Y = 0; Y < desired_image_size.Height; Y++)
                {
                    if (temp[Y, X, 0] == 0xFF && temp[Y, X, 1] == 0xFF && temp[Y, X, 2] == 0xFF)
                    {
                        F = F + 1;
                        Sx = Sx + Y;
                        Sy = Sy + X;
                        Jx = Jx + Math.Pow(Y, 2);
                        Jy = Jy + Math.Pow(X, 2);
                        Jxy = Jxy + X * Y;
                    }
                }
            }
            //Obliczenie środka cieżkości
            if (F > 0)
            {
                x0 = Sy / F;
                y0 = Sx / F;
                srodek_ciezkosci.X = (int)x0;
                srodek_ciezkosci.Y = (int)y0;
            }
        }

        private void Narysuj_wybrany_obiekt(int nr)
        {
            image_PB2.SetZero();
            byte[,,] temp1 = image_PB1.Data;
            byte[,,] temp2 = image_PB2.Data;

            MCvScalar kolor = new MCvScalar();
            //kolor.V0 = kolor_wypalenia.V0 + 8 + nr;
           // kolor.V1 = kolor_wypalenia.V1 + 8 + nr;
            //kolor.V2 = kolor_wypalenia.V2 + 8 + nr;

            kolor.V0 = kolor_wypalenia.V0 + nr;
            kolor.V1 = kolor_wypalenia.V1 + nr;
            kolor.V2 = kolor_wypalenia.V2 + nr;

            for (int y = 1; y < desired_image_size.Height - 2; y++)
            {
                for (int x = 1; x < desired_image_size.Width - 2; x++)
                {
                    if (temp1[y, x, 0] == kolor.V0 && temp1[y, x, 1] == kolor.V1 && temp1[y, x, 2] == kolor.V2)
                    {
                        temp2[y, x, 0] = 0xff;
                        temp2[y, x, 1] = 0xff;
                        temp2[y, x, 2] = 0xff;
                    }
                }
            }

            image_PB2.Data = temp2;
            pictureBox2.Image = image_PB2.Bitmap;
        }

        #endregion


        #region Wyznaczanie i rysowanie sygnatury

        private void draw_example_rays()
        {
            MCvScalar krok_koloru, aktualny_kolor;
            double krok_katowy, aktualny_kat, dlugosc;
            bool spowolnij;
            Point P = new Point(desired_image_size.Width / 2, desired_image_size.Height / 2);

            spowolnij = checkBox_Enable_slowdown.Checked;
            aktualny_kat = kat_poczatkowy * (Math.PI / 180);
            dlugosc = 100;

            if (radioButton_Draw_clockwise.Checked)
                krok_katowy = (2 * Math.PI / liczba_promieni);
            else
                krok_katowy = -(2 * Math.PI / liczba_promieni);

            aktualny_kolor = kolor_start;
            krok_koloru.V0 = (kolor_stop.V0 - kolor_start.V0) / liczba_promieni;
            krok_koloru.V1 = (kolor_stop.V1 - kolor_start.V1) / liczba_promieni;
            krok_koloru.V2 = (kolor_stop.V2 - kolor_start.V2) / liczba_promieni;

            for (int i = 0; i < liczba_promieni; i++)
            {
                double sin, cos;
                double ray_end_X, ray_end_Y;
                Point P2;
                sin = Math.Sin(aktualny_kat);
                cos = Math.Cos(aktualny_kat);
                ray_end_X = P.X + dlugosc * cos;
                ray_end_Y = P.Y + dlugosc * sin;
                P2 = new Point((int)ray_end_X, (int)ray_end_Y);
                CvInvoke.Line(image_PB1, P, P2, aktualny_kolor, 1);
                //Wyswietlanie wyników bierzącego kroku
                pictureBox1.Image = image_PB1.Bitmap;
                Application.DoEvents();
                if (spowolnij)
                {
                    Thread.Sleep(opoznienie_rysowania);
                }
                //Aktualizacja wartości dla następnego obiegu pętli
                aktualny_kolor.V0 += krok_koloru.V0;
                aktualny_kolor.V1 += krok_koloru.V1;
                aktualny_kolor.V2 += krok_koloru.V2;
                aktualny_kat += krok_katowy;
                //Przerwanie operacji rysowania
                if (draw_example_abort_signal)
                    break;
            }
            //Czyszczenie po ewentualnym przerwaniu
            if (draw_example_abort_signal)
            {
                draw_example_abort_signal = false;
                draw_example_option_selector = false;
                button_Draw_example_rays.Text = "Generuj przykladowe promienie";
            }

            pictureBox1.Image = image_PB1.Bitmap;
        }

        private double[] sygnatura_roznicowa(double[] rays, int dr)
        {
            double[] srednie = new double[rays.Length];
            int maxID = rays.Length - 1;
            for (int r = 0; r < rays.Length; r++)
            {
                double srednia = 0;
                int id = r + dr;
                srednia += rays[modulo(id, maxID)];
                id = r - dr;
                srednia += rays[modulo(id, maxID)];
                srednia /= 2;
                srednie[r] = Math.Abs(rays[r] - srednia);
            }

            return srednie;
        }

        private double[] sygnatura_radialna(Point start)
        {
            MCvScalar kolor_promienia = new MCvScalar();
            double[,] katy_kolejnych_promieni = new double[liczba_promieni, 2];
            double[] promienie = new double[liczba_promieni];
            double krok_katowy, aktualny_kat;

            generuj_losowy_kolor(ref kolor_promienia);
            aktualny_kat = kat_poczatkowy * (Math.PI / 180);

            if (radioButton_Draw_clockwise.Checked)
                krok_katowy = (2 * Math.PI / liczba_promieni);
            else
                krok_katowy = -(2 * Math.PI / liczba_promieni);

            for (int i = 0; i < liczba_promieni; i++)
            {
                katy_kolejnych_promieni[i, 0] = Math.Cos(aktualny_kat);
                katy_kolejnych_promieni[i, 1] = Math.Sin(aktualny_kat);
                aktualny_kat += krok_katowy;
            }

            image_PB2.SetZero();
            byte[, ,] temp1 = image_PB1.Data;
            int zakres = (int)Math.Sqrt(Math.Pow(desired_image_size.Width, 2) + Math.Pow(desired_image_size.Height, 2));
            for (int p = 0; p < liczba_promieni; p++)
            {
                for (int d = 0; d < zakres; d++)
                {
                    Point cp = new Point();
                    int dx, dy;
                    dx = (int)(d * katy_kolejnych_promieni[p, 0]);
                    dy = (int)(d * katy_kolejnych_promieni[p, 1]);
                    if (Math.Abs(dx) < zakres && Math.Abs(dy) < zakres)
                    {
                        cp.X = start.X + dx;
                        cp.Y = start.Y + dy;
                        if (temp1[cp.Y, cp.X, 0] == 0x00)
                        {
                            CvInvoke.Line(image_PB2, start, cp, kolor_promienia, 1);
                            promienie[p] = Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2));
                            break;
                        }
                    }
                }
            }

            pictureBox2.Image = image_PB2.Bitmap;

            return promienie;
        }

        private void namaluj_dane_z_tabeli(double[] dane, double[] krzywa, MCvScalar kolor, TrybRysowania tryb)
        {
            int w, h;
            int rX, rY, rW, rH;
            double sX, sY;
            w = pictureBox3.Width;
            h = pictureBox3.Height;
            sX = ((double)w / (double)liczba_promieni);//Dopasowanie szerokości
            sY = (((double)h - margines_na_tekst) / Math.Max(dane.Max(), 10));//Dopasowanie wysokości
            rX = rY = rW = rH = 0;

            if (tryb != TrybRysowania.TYLKO_KRZYWA)
            {
                for (int p = 0; p < liczba_promieni; p++)
                {
                    Rectangle r;
                    rX = (int)(sX * (double)p);
                    rW = ((int)(sX * (double)(p + 1))) - rX;

                    //Wybor rysowania
                    if (tryb == TrybRysowania.NAD_KRZYWA)
                    {
                        rY = (int)(h - sY * dane[p]);
                        rH = (int)((dane[p] - krzywa[p]) * sY) + 1;
                        if (rH < 0)
                            continue;
                    }
                    else if (tryb == TrybRysowania.TYLKO_DANE)
                    {
                        rY = (int)(h - sY * dane[p]);
                        rH = (int)(sY * dane[p]);
                    }

                    r = new Rectangle(rX, rY, rW, rH);
                    CvInvoke.Rectangle(image_PB3, r, kolor, -1);
                }
            }
            else
            {
                for (int p = 0; p < liczba_promieni - 1; p++)
                {
                    Point P1, P2;
                    int curr_x, next_x;
                    curr_x = (int)(sX * (double)p);
                    next_x = (int)(sX * (double)(p + 1));
                    P1 = new Point(curr_x, (int)(h - (int)(sY * krzywa[p])));
                    P2 = new Point(next_x, (int)(h - (int)(sY * krzywa[p + 1])));
                    CvInvoke.Line(image_PB3, P1, P2, kolor, 1);
                }
            }


            pictureBox3.Image = image_PB3.Bitmap;
        }
        
        #endregion

        #region Przekształcanie i analiza sygnatury

        private void usrednianie_wykresu()
        {
            MCvScalar kolor_nad_srednia = new MCvScalar(0, 255, 255);

            tabela_wartosci_srednich = wylicz_srednia_z_sygnatury(tabela_promieni);

            if (radioButton_Average_constant.Checked)
                kolor_nad_srednia = new MCvScalar(0, 255, 255);
            else if (radioButton_Average_minmax.Checked)
                kolor_nad_srednia = new MCvScalar(255, 0, 255);
            else if (radioButton_Average_moving.Checked)
                kolor_nad_srednia = new MCvScalar(0, 100, 255);

            if ((radioButton_Average_constant.Checked || radioButton_Average_moving.Checked) &&
                checkBox_Mix_averages.Checked)
            {
                kolor_nad_srednia = new MCvScalar(0, 255, 0);
            }

            namaluj_dane_z_tabeli(tabela_promieni, tabela_wartosci_srednich, kolor_nad_srednia, TrybRysowania.NAD_KRZYWA);
            namaluj_dane_z_tabeli(tabela_promieni, tabela_wartosci_srednich, kolor_nad_srednia, TrybRysowania.TYLKO_KRZYWA);
        }

        private double[] wylicz_srednia_z_sygnatury(double[] data)
        {
            double[] srednia = new double[data.Length];
            bool mix_averages = checkBox_Mix_averages.Checked;

            if (radioButton_Average_minmax.Checked)
            {
                double avg = (data.Max() + data.Min()) / 2.0;
                for (int i = 0; i < data.Length; i++)
                {
                    srednia[i] = avg;
                }
            }
            else if (mix_averages == false)
            {
                if (radioButton_Average_moving.Checked)
                {
                    int avg_width = (int)numericUpDown_Moving_Average.Value;
                    int maxID = data.Length - 1;
                    for (int i = 0; i < data.Length; i++)
                    {
                        double avg = 0;
                        int nr = 0;
                        int id = 0;
                        for (int f = -avg_width; f <= avg_width; f++)
                        {
                            nr++;
                            id = i + f;
                            avg += data[modulo(id, maxID)];
                        }
                        avg /= nr;
                        srednia[i] = avg;
                    }
                }
                else if (radioButton_Average_constant.Checked)
                {
                    double avg = 0;
                    for (int i = 0; i < data.Length; i++)
                    {
                        avg += data[i];
                    }
                    avg /= (data.Length);
                    for (int i = 0; i < data.Length; i++)
                    {
                        srednia[i] = avg;
                    }
                }
            }
            else if (mix_averages == true)
            {
                double avg_C = 0;
                double ratio = (double)numericUpDown_Average_C2M_weight.Value;
                for (int i = 0; i < data.Length; i++)
                {
                    avg_C += data[i];
                }
                avg_C /= (data.Length);

                int avg_width = (int)numericUpDown_Moving_Average.Value;
                int maxID = data.Length - 1;
                for (int i = 0; i < data.Length; i++)
                {
                    double avg_M = 0;
                    int nr = 0;
                    int id = 0;
                    for (int f = -avg_width; f <= avg_width; f++)
                    {
                        nr++;
                        id = i + f;
                        avg_M += data[modulo(id, maxID)];
                    }
                    avg_M /= nr;
                    srednia[i] = ((avg_C * ratio) + (avg_M * (1 - ratio)));
                }

            }

            return srednia;
        }

        private void licz_wierzcholki(double[] dane, double[] krzywa)
        {
            double sX;
            int przeskok = (liczba_promieni / 15);
            int wierzcholki = 0;
            sX = ((double)pictureBox3.Width / (double)liczba_promieni);//Dopasowanie szerokości

            for (int i = 0; i < liczba_promieni - 1; i++)
            {
                if (dane[i] < krzywa[i] && dane[i + 1] >= krzywa[i + 1])
                {
                    wierzcholki++;
                    CvInvoke.Line(image_PB3, new Point((int)(i * sX), pictureBox3.Height), new Point((int)(i * sX), 40), new MCvScalar(0, 255, 0), 1);
                    i += przeskok;
                    CvInvoke.Line(image_PB3, new Point((int)(i * sX), pictureBox3.Height), new Point((int)(i * sX), 50), new MCvScalar(255, 255, 0), 1);
                }
            }
            textBox_LW.Text = wierzcholki.ToString();
            pictureBox3.Image = image_PB3.Bitmap;
        }

        #endregion

        #region Filtrowanie sygnatury

        private double filtr_usredniajacy(double[] data, FilterParams param)
        {
            double ret = 0;
            int maxID = data.Length - 1;
            int nr = 0;

            for (int f = -param.radius_minmax; f <= param.radius_minmax; f++)
            {
                nr++;
                int id = param.index + f;
                ret += data[modulo(id, maxID)];
            }
            ret /= nr;

            return ret;
        }

        private double filtr_min_max(double[] data, FilterParams param)
        {
            double ret = 0;
            int maxID = data.Length - 1;

            ret = data[param.index];

            for (int f = -param.radius_minmax; f <= param.radius_minmax; f++)
            {
                int id = param.index + f;
                double val = data[modulo(id, maxID)];
                if (param.isMax)
                    ret = Math.Max(ret, val);
                else
                    ret = Math.Min(ret, val);
            }

            return ret;
        }

        private double filtr_binarny(double[] data, FilterParams param)
        {
            double ret = 0;

            for (int i = 0; i < data.Length; i++)
            {
                ret = data[param.index];
                ret = ret >= param.binary_thresh[i] ? 150 : 0;
            }

            return ret;
        }

        private void filtruj_tabele(ref double[] rays, Filtr filtr, FilterParams param)
        {
            double[] input = new double[rays.Length];
            for (int i = 0; i < rays.Length; i++)
                input[i] = rays[i];

            for (int i = 0; i < rays.Length; i++)
            {
                param.index = i;
                rays[i] = filtr(input, param);
            }
        }

        #endregion

        #region Interakcja z interfejsem programu

        private void odswiez_parametry_filtru()
        {
            parametry_filtru.radius_average = (int)numericUpDown_Filter_width.Value;
            parametry_filtru.radius_minmax = (int)numericUpDown_Filter_width.Value;
            parametry_filtru.isMax = radioButton_Max.Checked;
            parametry_filtru.binary_thresh = tabela_wartosci_srednich;
        }

        private void pictureBox2_Click(object sender, EventArgs e)
        {

        }

        private void refresh_GUI_settings()
        {
            liczba_promieni = (int)numericUpDown_Ray_count.Value;
            opoznienie_rysowania = (int)numericUpDown_Ray_slowdown.Value;
            kat_poczatkowy = (int)numericUpDown_Start_angle.Value;
        }

        private void generuj_losowy_kolor(ref MCvScalar kolor)
        {
            kolor.V0 = rnd.Next(0, 255);
            kolor.V1 = rnd.Next(0, 255);
            kolor.V2 = rnd.Next(0, 255);
        }

        private int modulo(int a, int b)
        {
            return (Math.Abs(a * b) + a) % b;
        }

        private void czysc_obraz(Image<Bgr, byte> im, PictureBox PB)
        {
            im.SetZero();
            PB.Image = im.Bitmap;
        }

        private Bitmap get_image_bitmap_from_file(string path, ref Image<Bgr, byte> Data)
        {
            try
            {
                Mat temp = CvInvoke.Imread(path);
                CvInvoke.Resize(temp, temp, desired_image_size);
                Data = temp.ToImage<Bgr, byte>();
                return Data.Bitmap;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Podana ścieżka jest nieprawidłowa");
                return null;
            }
        }


        private string get_image_path()
        {
            string ret = "";
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Filter = "Obrazy|*.jpg;*.jpeg;*.png;*.bmp";
            openFileDialog1.Title = "Wybierz obrazek.";
            //Jeśli wszystko przebiegło ok to pobiera nazwę pliku
            if (openFileDialog1.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                ret = openFileDialog1.FileName;
            }

            return ret;
        }

        #endregion
    }

    public struct FilterParams
    {
        public double[] binary_thresh;
        public int radius_minmax;
        public int radius_average;
        public int index;
        public bool isMax;
    }
}
