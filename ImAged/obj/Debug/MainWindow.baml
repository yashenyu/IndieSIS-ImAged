   M S B A M L   `   `   `  ����A  =ImAged, Version=1.0.0.0, Culture=neutral, PublicKeyToken=nullclr-namespace:ImAgedImAged  5   	   =#clr-namespace:ImAged.MVVM.ViewModelImAged.MVVM.ViewModel  5   	       ImAged.MainWindow   5   	   R NWindowsBase, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35W SPresentationCore, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35\ XPresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35D 9http://schemas.microsoft.com/winfx/2006/xaml/presentation    5   	   R NSystem.Xaml, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089:x,http://schemas.microsoft.com/winfx/2006/xaml     5   	   @d2http://schemas.microsoft.com/expression/blend/2008     5   	   Jmc;http://schemas.openxmlformats.org/markup-compatibility/2006     5   	   &localclr-namespace:ImAged     5   	   9	viewModel#clr-namespace:ImAged.MVVM.ViewModel     5   	     � Title$  
MainWindow��5	   	   $	��600��6   $	��920��6)    � WindowStyle$
 None=�5
   	    � 
ResizeMode$ NoResize=�5   	   
������  � AllowsTransparency .  � DataContext 5      )  #ImAged.MVVM.ViewModel.MainViewModel  5   
   .��5      �� ���#272537 �� CornerRadius$	  20��5      ._�5   
   �  � ColumnDefinitions 5      �� 5      $	��200 �6#   �� 5       � RowDefinitions 5      �� 5      $��75 �6    �� 5      .+�5       �� $��ImAged��6   $��Center=�5!      $
��Left=�5"      
�������$��22(�5$      $��20,0,0,0q�5%      �� 5'      $��1��6   .+�5(      	� $
��Home��6   $��50��5)      
�������$��14(�5+         MenuButtonTheme#��[  5,       d� 	IsChecked$
 TrueY�5-      ��5.      �� *HomeViewCommand+	� 50      $
��View��6   $��50��51      
�������$��14(�53      #��[  54      ��55      �� *ViewViewCommand+	� 57      $��	Favorites��6   $��50��58      
�������$��14(�5:      #��[  5;      	� 5=      $��Tools��6   $��50��5>      
�������$��14(�5@      #��[  5A      5D      �� 5E      $	��250��6   $��40��5F      $��Center=�5G      $
��Left=�5H      $��5q�5I      $��1��5J        ModernTextbox#��[ 5K      �� 5M      $��1��6   $��1��5N      $��10q�5O      ��5P      �� *CurrentView+5S      5U      5W      